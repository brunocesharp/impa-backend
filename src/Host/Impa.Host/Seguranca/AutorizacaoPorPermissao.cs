using System.Security.Claims;
using Impa.Modules.Acesso.Application.Autenticacao;
using Impa.Modules.Acesso.Application.Liberacoes;
using Impa.Modules.Acesso.Contracts;
using Impa.Modules.Acesso.Infrastructure.Sessoes;
using Microsoft.AspNetCore.Authorization;

namespace Impa.Host.Seguranca;

/// <summary>Permissões do usuário da requisição, carregadas do banco por <see cref="AutorizacaoPorPermissao.UsePermissoesDaSessao"/>.</summary>
public sealed record PermissoesDaRequisicao(PermissoesCarregadas Carregadas);

/// <summary>Exige a permissão (código do catálogo) no perfil atual do usuário.</summary>
public sealed record PermissaoRequirement(string Permissao) : IAuthorizationRequirement;

/// <summary>
/// Corpo do 403 por falta de permissão (story 1-5, T1). <paramref name="Liberavel"/>: um administrador pode liberar esta
/// operação (RN-1.9) — a SPA abre o diálogo de liberação. Sem <c>mensagem</c>: cada tela mantém o próprio texto do 403.
/// </summary>
public sealed record PermissaoRequeridaDto(string Codigo, string Permissao, bool Liberavel);

/// <summary>Liberação consumida nesta requisição (story 1-5): quem liberou e a permissão liberada.</summary>
public sealed record LiberacaoDaRequisicao(Guid LiberadorId, string Permissao);

/// <summary>
/// RN-1.10: a permissão é verificada no servidor, contra o perfil atual no banco (story 1.3). RN-1.9 (story 1-5): sem a
/// permissão, uma permissão liberável passa com o token de liberação no cabeçalho <c>X-Liberacao</c> — de uso único,
/// da sessão do operador e daquela permissão. O token é consumido e a liberação registrada com a operação (rota).
/// </summary>
public sealed class PermissaoAuthorizationHandler : AuthorizationHandler<PermissaoRequirement>
{
    public const string CabecalhoLiberacao = "X-Liberacao";

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissaoRequirement requirement)
    {
        if (context.Resource is not HttpContext http)
            return;

        if (http.PermissoesDaSessao().Tem(requirement.Permissao))
        {
            context.Succeed(requirement);
            return;
        }

        if (await ConsumirLiberacaoAsync(http, requirement.Permissao) is { } liberadorId)
        {
            http.Features.Set(new LiberacaoDaRequisicao(liberadorId, requirement.Permissao));
            context.Succeed(requirement);
        }
    }

    private static async Task<Guid?> ConsumirLiberacaoAsync(HttpContext http, string permissao)
    {
        if (!Permissoes.EhLiberavel(permissao)
            || TokenDeLiberacao.HashSeValido(http.Request.Headers[CabecalhoLiberacao].ToString()) is not { } hash
            || http.Features.Get<SessaoDaRequisicao>() is not { } sessao)
            return null;

        var servicos = http.RequestServices;
        var liberacoes = servicos.GetRequiredService<ILiberacoes>();
        if (await liberacoes.ObterLiberadorAsync(hash, sessao.Id, permissao, http.RequestAborted) is not { } liberadorId)
            return null;

        // O liberador pode ter perdido a permissão (ou sido desativado) depois de liberar: a liberação não vale mais.
        var doLiberador = await servicos.GetRequiredService<IPermissoesDoUsuario>().ObterAsync(liberadorId, http.RequestAborted);
        if (doLiberador.TrocaDeSenhaObrigatoria || !doLiberador.Tem(Permissoes.LiberarOperacoes) || !doLiberador.Tem(permissao))
            return null;

        var operacao = $"{http.Request.Method} {http.Request.Path}";
        return await liberacoes.ConsumirAsync(hash, sessao.Id, permissao, operacao, http.RequestAborted);
    }
}

/// <summary>
/// <see cref="IContextoDeAcesso"/> da requisição HTTP: o usuário da sessão e, se a operação foi liberada, quem liberou.
/// </summary>
public sealed class ContextoDeAcesso(IHttpContextAccessor acessor) : IContextoDeAcesso
{
    public Guid? UsuarioId =>
        Guid.TryParse(acessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public Guid? Liberador => acessor.HttpContext?.Features.Get<LiberacaoDaRequisicao>()?.LiberadorId;
}

/// <summary>RN-1.10: as permissões são verificadas no servidor; esconder um botão na tela não basta.</summary>
public static class AutorizacaoPorPermissao
{
    /// <summary><c>codigo</c> do 403 quando falta uma permissão (story 1-5).</summary>
    public const string CodigoPermissaoRequerida = "permissao_requerida";

    /// <summary>
    /// A permissão que faltou na autorização, para o 403 <c>permissao_requerida</c>; <c>null</c> quando a falha não é de
    /// permissão. Liberável só se a permissão é liberável no catálogo e é a única exigência que falhou: sem terminal
    /// registrado, por exemplo, liberar não resolveria.
    /// </summary>
    public static PermissaoRequeridaDto? PermissaoQueFalta(AuthorizationFailure? falha)
    {
        if (falha is not { FailCalled: false })
            return null;

        var falharam = falha.FailedRequirements.ToList();
        if (falharam.OfType<PermissaoRequirement>().FirstOrDefault() is not { } permissao)
            return null;

        var liberavel = falharam.Count == 1 && Permissoes.EhLiberavel(permissao.Permissao);
        return new PermissaoRequeridaDto(CodigoPermissaoRequerida, permissao.Permissao, liberavel);
    }

    /// <summary>Negar por padrão: todo endpoint exige sessão, exceto os marcados com <c>AllowAnonymous</c>.</summary>
    public static IServiceCollection AddAutorizacaoPorPermissao(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        services.AddSingleton<IAuthorizationHandler, PermissaoAuthorizationHandler>();
        services.AddScoped<IContextoDeAcesso, ContextoDeAcesso>(); // operador e quem liberou (story 1-5, T4)

        return services;
    }

    /// <summary>
    /// Depois da autenticação: carrega o perfil e as permissões atuais do usuário (banco, com cache curto) para a
    /// autorização e para o JSON (<c>ExigePermissao</c>). Mudança de perfil vale na próxima requisição, sem novo login.
    /// </summary>
    public static IApplicationBuilder UsePermissoesDaSessao(this IApplicationBuilder app) =>
        app.Use(async (contexto, proximo) =>
        {
            var carregadas = PermissoesCarregadas.Nenhuma;
            if (contexto.User.Identity?.IsAuthenticated == true
                && Guid.TryParse(contexto.User.FindFirstValue(ClaimTypes.NameIdentifier), out var usuarioId))
            {
                carregadas = await contexto.RequestServices.GetRequiredService<IPermissoesDoUsuario>()
                    .ObterAsync(usuarioId, contexto.RequestAborted);
            }

            contexto.Features.Set(new PermissoesDaRequisicao(carregadas));
            await proximo(contexto);
        });

    /// <summary>Permissões do usuário desta requisição (nenhuma sem sessão).</summary>
    public static PermissoesCarregadas PermissoesDaSessao(this HttpContext contexto) =>
        contexto.Features.Get<PermissoesDaRequisicao>()?.Carregadas ?? PermissoesCarregadas.Nenhuma;

    /// <summary>Exige sessão e a permissão indicada no perfil atual do usuário. Sem ela: 403.</summary>
    public static TBuilder RequirePermissao<TBuilder>(this TBuilder builder, string permissao)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(politica => politica
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissaoRequirement(permissao)));
}
