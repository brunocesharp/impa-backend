using System.Security.Claims;
using Impa.Host.Http;
using Impa.Host.Seguranca;
using Impa.Modules.Acesso.Application.Liberacoes.Commands.LiberarOperacao;
using Impa.Modules.Acesso.Infrastructure.Sessoes;
using MediatR;

namespace Impa.Host.Endpoints;

public sealed record LiberarOperacaoRequest(string? Usuario, string? Senha, string? Permissao)
{
    // A senha nunca vai para log (story 1-5, T3).
    public override string ToString() =>
        $"{nameof(LiberarOperacaoRequest)} {{ Usuario = {Usuario}, Senha = ***, Permissao = {Permissao} }}";
}

/// <summary><c>/api/acesso/liberacoes</c>: liberação de operação por administrador (story 1-5, RN-1.9).</summary>
public static class LiberacoesEndpoints
{
    public static IEndpointRouteBuilder MapLiberacoesEndpoints(this IEndpointRouteBuilder app)
    {
        // Só a sessão do operador (fallback): quem libera é conferido pela credencial informada, não pela sessão.
        app.MapPost("/api/acesso/liberacoes", Liberar)
            // Confere usuário e senha como o login: conta no mesmo limite por IP (story 1-10; revisão 1 da 1-5, R1).
            .RequireRateLimiting(LimiteDeLogin.Politica)
            .WithTags("Acesso")
            .WithName("LiberarOperacao")
            .WithSummary("Administrador informa usuário e senha no terminal do operador e libera uma operação")
            .Produces<LiberacaoDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            // Credencial inválida, usuário bloqueado ou sem permissão para liberar: 403 { mensagem } — não 401,
            // porque a sessão do operador continua válida.
            .Produces<ErroDto>(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status401Unauthorized)
            // Limite por IP do login: 429 { mensagem } com Retry-After, sem chegar ao caso de uso.
            .Produces<ErroDto>(StatusCodes.Status429TooManyRequests);

        return app;
    }

    private static async Task<IResult> Liberar(LiberarOperacaoRequest request, ISender mediator, HttpContext contexto, CancellationToken ct)
    {
        if (!Guid.TryParse(contexto.User.FindFirstValue(ClaimTypes.NameIdentifier), out var operadorId)
            || contexto.Features.Get<SessaoDaRequisicao>() is not { } sessao)
            return Results.Unauthorized();

        var resultado = await mediator.Send(new LiberarOperacaoCommand(
            request.Usuario ?? string.Empty,
            request.Senha ?? string.Empty,
            request.Permissao ?? string.Empty,
            operadorId,
            sessao.Id), ct);

        return resultado.ParaHttp();
    }
}
