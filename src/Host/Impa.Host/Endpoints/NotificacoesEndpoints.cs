using System.Security.Claims;
using Impa.Host.Http;
using Impa.Host.Seguranca;
using Impa.Modules.Acesso.Contracts;
using Impa.Modules.Configuracao.Application.Notificacoes;
using Impa.Modules.Configuracao.Application.Notificacoes.Commands.MarcarComoLida;
using Impa.Modules.Configuracao.Application.Notificacoes.Commands.MarcarTodasComoLidas;
using Impa.Modules.Configuracao.Application.Notificacoes.Queries.ContarNaoLidas;
using Impa.Modules.Configuracao.Application.Notificacoes.Queries.ListarNotificacoes;
using MediatR;

namespace Impa.Host.Endpoints;

/// <summary>
/// <c>/api/notificacoes</c>: central de notificações do usuário da sessão (story 4.2, RN-2.3). Tudo exige
/// <c>notificacoes.receber</c>; cada usuário vê e marca só as próprias.
/// </summary>
public static class NotificacoesEndpoints
{
    public static IEndpointRouteBuilder MapNotificacoesEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/notificacoes")
            .WithTags("Notificações")
            .RequirePermissao(Permissoes.NotificacoesReceber); // sem sessão 401; sem a permissão 403

        grupo.MapGet("/", Listar)
            .WithName("ListarNotificacoes")
            .WithSummary("Notificações do usuário da sessão, mais recentes primeiro (pagina, tamanho: padrão 1 e 20, máximo 100)")
            .Produces<PaginaDeNotificacoesDto>()
            .ProducesValidationProblem();

        grupo.MapGet("/nao-lidas/contagem", ContarNaoLidas)
            .WithName("ContarNotificacoesNaoLidas")
            .WithSummary("Quantas notificações o usuário da sessão ainda não leu (contador do sino)")
            .Produces<ContagemDeNaoLidasDto>();

        grupo.MapPost("/{id:guid}/lida", MarcarComoLida)
            .WithName("MarcarNotificacaoComoLida")
            .WithSummary("Marca a notificação como lida só para o usuário da sessão")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErroDto>(StatusCodes.Status404NotFound);

        grupo.MapPost("/lidas", MarcarTodasComoLidas)
            .WithName("MarcarTodasAsNotificacoesComoLidas")
            .WithSummary("Marca todas as notificações do usuário da sessão como lidas")
            .Produces(StatusCodes.Status204NoContent);

        return app;
    }

    private static Guid UsuarioDaSessao(ClaimsPrincipal usuario) =>
        Guid.Parse(usuario.FindFirstValue(ClaimTypes.NameIdentifier)!); // a política já exige a sessão

    private static async Task<IResult> Listar(
        ClaimsPrincipal usuario, ISender mediator, CancellationToken ct, int? pagina = null, int? tamanho = null) =>
        (await mediator.Send(new ListarNotificacoesQuery(
            UsuarioDaSessao(usuario), pagina ?? 1, tamanho ?? ListarNotificacoesQuery.TamanhoPadraoDaPagina), ct)).ParaHttp();

    private static async Task<IResult> ContarNaoLidas(ClaimsPrincipal usuario, ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new ContarNaoLidasQuery(UsuarioDaSessao(usuario)), ct)).ParaHttp();

    private static async Task<IResult> MarcarComoLida(Guid id, ClaimsPrincipal usuario, ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new MarcarComoLidaCommand(id, UsuarioDaSessao(usuario)), ct)).ParaHttp();

    private static async Task<IResult> MarcarTodasComoLidas(ClaimsPrincipal usuario, ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new MarcarTodasComoLidasCommand(UsuarioDaSessao(usuario)), ct)).ParaHttp();
}
