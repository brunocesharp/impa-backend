using Impa.Host.Http;
using Impa.Host.Seguranca;
using Impa.Modules.Acesso.Contracts;
using Impa.Modules.Configuracao.Application.Auditoria;
using Impa.Modules.Configuracao.Application.Auditoria.Queries.ConsultarAuditoria;
using MediatR;

namespace Impa.Host.Endpoints;

/// <summary>
/// <c>/api/auditoria</c>: consulta das operações sensíveis (M1.6, story 4.4) — quem alterou o quê e quando, com os
/// valores antes e depois. Exige <c>auditoria.ver</c>.
/// </summary>
public static class AuditoriaEndpoints
{
    public static IEndpointRouteBuilder MapAuditoriaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/auditoria", Consultar)
            .RequirePermissao(Permissoes.AuditoriaVer) // sem sessão 401; sem a permissão 403
            .WithTags("Auditoria")
            .WithName("ConsultarAuditoria")
            .WithSummary("Operações sensíveis, mais recentes primeiro. Filtros: de (inclusive) e ate (exclusive) em ISO 8601, " +
                         "usuario, modulo e entidade (trecho, sem diferenciar maiúsculas); pagina e tamanho (padrão 1 e 20, máximo 100)")
            .Produces<PaginaDeAuditoriaDto>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> Consultar(
        ISender mediator, CancellationToken ct,
        DateTimeOffset? de = null, DateTimeOffset? ate = null, string? usuario = null, string? modulo = null,
        string? entidade = null, int? pagina = null, int? tamanho = null) =>
        (await mediator.Send(new ConsultarAuditoriaQuery(
            de, ate, usuario, modulo, entidade,
            pagina ?? 1, tamanho ?? ConsultarAuditoriaQuery.TamanhoPadraoDaPagina), ct)).ParaHttp();
}
