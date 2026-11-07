using Impa.Host.Http;
using Impa.Host.Messaging;
using Impa.Host.Seguranca;
using Impa.Modules.Acesso.Contracts;

namespace Impa.Host.Endpoints;

/// <summary><c>/api/mensageria</c>: endpoint interno de operação do despachante (ADR-011). Sem tela nesta fase.</summary>
public static class MensageriaEndpoints
{
    public static IEndpointRouteBuilder MapMensageriaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/mensageria/eventos/{id:guid}/reprocessar", Reprocessar)
            .RequirePermissao(Permissoes.ConfiguracaoVer)
            .WithTags("Mensageria")
            .WithName("ReprocessarEvento")
            .WithSummary("Volta um evento com falha para a fila de entrega (exige configuracao.ver)")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErroDto>(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> Reprocessar(Guid id, ReprocessadorDeEventos reprocessador, CancellationToken ct) =>
        await reprocessador.ReprocessarAsync(id, ct)
            ? Results.NoContent()
            : Results.Json(new ErroDto("Evento com falha não encontrado."), statusCode: StatusCodes.Status404NotFound);
}
