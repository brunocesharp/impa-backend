using System.Security.Claims;
using Impa.Host.Http;
using Impa.Host.Seguranca;
using Impa.Modules.Acesso.Application.Terminais;
using Impa.Modules.Acesso.Application.Terminais.Commands.RegistrarTerminal;
using Impa.Modules.Acesso.Application.Terminais.Commands.RenomearTerminal;
using Impa.Modules.Acesso.Application.Terminais.Commands.RevogarTerminal;
using Impa.Modules.Acesso.Application.Terminais.Queries.ListarTerminais;
using Impa.Modules.Acesso.Contracts;
using MediatR;

namespace Impa.Host.Endpoints;

public sealed record TerminalRequest(string? Nome);

/// <summary>
/// <c>/api/terminais</c>: computadores e celulares registrados para o PDV e o caixa (story 1-6, RN-11.1).
/// Gerenciar exige <c>terminais.gerenciar</c>; <c>/atual</c> só a sessão e o terminal deste dispositivo.
/// </summary>
public static class TerminaisEndpoints
{
    public static IEndpointRouteBuilder MapTerminaisEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/terminais").WithTags("Terminais");

        // Fora do grupo de gestão: qualquer usuário com sessão pergunta se este dispositivo é terminal (guard do PDV).
        grupo.MapGet("/atual", ObterAtual)
            .RequireTerminal()
            .WithName("ObterTerminalAtual")
            .WithSummary("Terminal deste dispositivo; sem terminal válido 403 { codigo: \"terminal_nao_registrado\" }")
            .Produces<TerminalAtualDto>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces<ErroDto>(StatusCodes.Status403Forbidden);

        var gestao = app.MapGroup("/api/terminais").WithTags("Terminais").RequirePermissao(Permissoes.TerminaisGerenciar); // sem sessão 401; sem a permissão 403

        gestao.MapGet("/", Listar)
            .WithName("ListarTerminais")
            .WithSummary("Terminais ativos e revogados; \"atual\" marca o deste dispositivo")
            .Produces<IReadOnlyList<TerminalDto>>();

        gestao.MapPost("/", Registrar)
            .WithName("RegistrarTerminal")
            .WithSummary("Registra este dispositivo como terminal e grava o token no cookie impa_terminal")
            .Produces<TerminalDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces<ErroDto>(StatusCodes.Status409Conflict);

        gestao.MapPut("/{id:guid}", Renomear)
            .WithName("RenomearTerminal")
            .WithSummary("Troca o nome de um terminal ativo")
            .Produces<TerminalDto>()
            .ProducesValidationProblem()
            .Produces<ErroDto>(StatusCodes.Status404NotFound)
            .Produces<ErroDto>(StatusCodes.Status409Conflict);

        gestao.MapPost("/{id:guid}/revogar", Revogar)
            .WithName("RevogarTerminal")
            .WithSummary("Revoga o terminal: o dispositivo deixa de acessar PDV e caixa")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErroDto>(StatusCodes.Status404NotFound);

        return app;
    }

    private static IResult ObterAtual(HttpContext contexto, TimeProvider relogio)
    {
        // A política já validou o token; renova o prazo do cookie a cada uso.
        TerminalRegistrado.GravarCookie(contexto, TerminalRegistrado.TokenDoDispositivo(contexto)!, relogio);
        return Results.Ok(contexto.TerminalDaRequisicao());
    }

    private static async Task<IResult> Listar(HttpContext contexto, ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new ListarTerminaisQuery(TerminalRegistrado.TokenDoDispositivo(contexto)), ct)).ParaHttp();

    private static async Task<IResult> Registrar(TerminalRequest request, HttpContext contexto, ISender mediator,
        TimeProvider relogio, CancellationToken ct)
    {
        _ = Guid.TryParse(contexto.User.FindFirstValue(ClaimTypes.NameIdentifier), out var usuarioId);
        var resultado = await mediator.Send(new RegistrarTerminalCommand(request.Nome ?? string.Empty, usuarioId,
            TerminalRegistrado.TokenDoDispositivo(contexto)), ct);
        if (!resultado.IsSuccess)
            return resultado.ParaHttp();

        TerminalRegistrado.GravarCookie(contexto, resultado.Value.Token, relogio);
        return Results.Created((string?)null, resultado.Value.Terminal); // o token não vai no corpo
    }

    private static async Task<IResult> Renomear(Guid id, TerminalRequest request, ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new RenomearTerminalCommand(id, request.Nome ?? string.Empty), ct)).ParaHttp();

    private static async Task<IResult> Revogar(Guid id, ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new RevogarTerminalCommand(id), ct)).ParaHttp();
}
