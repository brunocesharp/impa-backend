using System.Security.Claims;
using Impa.Modules.Acesso.Application.Autenticacao;
using Microsoft.AspNetCore.Authorization;

namespace Impa.Host.Seguranca;

/// <summary>Permissões do usuário da requisição, carregadas do banco por <see cref="AutorizacaoPorPermissao.UsePermissoesDaSessao"/>.</summary>
public sealed record PermissoesDaRequisicao(PermissoesCarregadas Carregadas);

/// <summary>Exige a permissão (código do catálogo) no perfil atual do usuário.</summary>
public sealed record PermissaoRequirement(string Permissao) : IAuthorizationRequirement;

/// <summary>RN-1.10: a permissão é verificada no servidor, contra o perfil atual no banco (story 1.3).</summary>
public sealed class PermissaoAuthorizationHandler : AuthorizationHandler<PermissaoRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissaoRequirement requirement)
    {
        if (context.Resource is HttpContext http
            && http.Features.Get<PermissoesDaRequisicao>()?.Carregadas.Tem(requirement.Permissao) == true)
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}

/// <summary>RN-1.10: as permissões são verificadas no servidor; esconder um botão na tela não basta.</summary>
public static class AutorizacaoPorPermissao
{
    /// <summary>Negar por padrão: todo endpoint exige sessão, exceto os marcados com <c>AllowAnonymous</c>.</summary>
    public static IServiceCollection AddAutorizacaoPorPermissao(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        services.AddSingleton<IAuthorizationHandler, PermissaoAuthorizationHandler>();

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
