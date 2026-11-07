using Impa.Host.Http;
using Impa.Modules.Acesso.Application.Terminais;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Impa.Host.Seguranca;

/// <summary>Exige que a requisição venha de um terminal registrado e não revogado (cookie <c>impa_terminal</c>).</summary>
public sealed class TerminalRequirement : IAuthorizationRequirement;

/// <summary>Terminal válido da requisição, guardado pela política para o endpoint usar sem consultar de novo.</summary>
public sealed record TerminalDaRequisicao(TerminalAtualDto Terminal);

/// <summary>
/// RN-11.1 (story 1-6): o token do terminal fica no cookie do dispositivo; o banco guarda só o hash. A cada
/// requisição protegida o terminal é conferido no banco, então revogar vale na próxima requisição.
/// </summary>
public sealed class TerminalAuthorizationHandler : AuthorizationHandler<TerminalRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, TerminalRequirement requirement)
    {
        if (context.Resource is not HttpContext http)
            return;

        if (http.Features.Get<TerminalDaRequisicao>() is null)
        {
            if (TokenDoTerminal.HashSeValido(TerminalRegistrado.TokenDoDispositivo(http)) is not { } hash
                || await http.RequestServices.GetRequiredService<ITerminais>().ObterValidoAsync(hash, http.RequestAborted) is not { } terminal)
                return;

            http.Features.Set(new TerminalDaRequisicao(terminal));
        }

        context.Succeed(requirement);
    }
}

/// <summary>
/// Quando só falta o terminal válido, a política responde 403 <c>{ mensagem, codigo: "terminal_nao_registrado" }</c> (a
/// SPA mostra a mensagem do cenário 11.6). Os demais resultados seguem o padrão (401 sem sessão, 403 sem permissão —
/// inclusive sem permissão e sem terminal).
/// </summary>
public sealed class RespostaDaAutorizacao : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _padrao = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden && SoFaltaOTerminal(authorizeResult.AuthorizationFailure))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new ErroDto(MensagensTerminais.NaoRegistrado, TerminalRegistrado.Codigo));
            return;
        }

        await _padrao.HandleAsync(next, context, policy, authorizeResult);
    }

    /// <summary>
    /// O 403 <c>terminal_nao_registrado</c> só quando registrar o terminal resolveria: se também falta a permissão da
    /// função (<c>PermissaoRequirement</c>) ou outra exigência, segue o 403 padrão (revisão 1, R2).
    /// </summary>
    private static bool SoFaltaOTerminal(AuthorizationFailure? falha) =>
        falha is { FailCalled: false }
        && falha.FailedRequirements.Any()
        && falha.FailedRequirements.All(r => r is TerminalRequirement);
}

public static class TerminalRegistrado
{
    public const string NomeCookie = "impa_terminal";
    public const string Codigo = "terminal_nao_registrado";

    /// <summary>
    /// "Longa duração": 400 dias é o máximo que os navegadores aceitam (Chrome limita a validade do cookie);
    /// o <c>GET /api/terminais/atual</c> renova o prazo a cada uso.
    /// </summary>
    public static readonly TimeSpan DuracaoDoCookie = TimeSpan.FromDays(400);

    public static IServiceCollection AddTerminalRegistrado(this IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationHandler, TerminalAuthorizationHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, RespostaDaAutorizacao>();
        return services;
    }

    /// <summary>
    /// Exige sessão e terminal registrado (PDV e caixa, épicos 5 e 6). Sem sessão: 401; sem terminal válido: 403
    /// <c>terminal_nao_registrado</c>. Combine com <c>RequirePermissao</c> para a permissão da função.
    /// </summary>
    public static TBuilder RequireTerminal<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(politica => politica
            .RequireAuthenticatedUser()
            .AddRequirements(new TerminalRequirement()));

    /// <summary>Token do terminal no cookie deste dispositivo (pode ser inválido ou de um terminal revogado).</summary>
    public static string? TokenDoDispositivo(HttpContext contexto) => contexto.Request.Cookies[NomeCookie];

    /// <summary>Terminal válido da requisição, depois de <see cref="RequireTerminal{TBuilder}"/>.</summary>
    public static TerminalAtualDto? TerminalDaRequisicao(this HttpContext contexto) =>
        contexto.Features.Get<TerminalDaRequisicao>()?.Terminal;

    /// <summary>Grava (ou renova) o token no cookie HttpOnly, Secure, SameSite=Strict do dispositivo.</summary>
    public static void GravarCookie(HttpContext contexto, string token, TimeProvider relogio) =>
        contexto.Response.Cookies.Append(NomeCookie, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            IsEssential = true,
            Expires = relogio.GetUtcNow().Add(DuracaoDoCookie),
        });
}
