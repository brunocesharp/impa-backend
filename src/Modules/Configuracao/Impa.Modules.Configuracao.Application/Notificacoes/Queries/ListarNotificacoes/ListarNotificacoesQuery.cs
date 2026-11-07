using Ardalis.Result;
using FluentValidation;
using MediatR;

namespace Impa.Modules.Configuracao.Application.Notificacoes.Queries.ListarNotificacoes;

/// <summary>Notificações do usuário da sessão, mais recentes primeiro, paginadas (painel do sino).</summary>
public sealed record ListarNotificacoesQuery(Guid UsuarioId, int Pagina, int TamanhoDaPagina)
    : IRequest<Result<PaginaDeNotificacoesDto>>
{
    public const int TamanhoPadraoDaPagina = 20;
    public const int TamanhoMaximoDaPagina = 100;
}

public sealed class ListarNotificacoesQueryValidator : AbstractValidator<ListarNotificacoesQuery>
{
    public ListarNotificacoesQueryValidator()
    {
        RuleFor(q => q.Pagina).GreaterThanOrEqualTo(1).WithMessage("A página começa em 1");
        RuleFor(q => q.TamanhoDaPagina)
            .InclusiveBetween(1, ListarNotificacoesQuery.TamanhoMaximoDaPagina)
            .WithMessage($"O tamanho da página vai de 1 a {ListarNotificacoesQuery.TamanhoMaximoDaPagina}");
    }
}

public sealed class ListarNotificacoesQueryHandler(INotificacoes notificacoes)
    : IRequestHandler<ListarNotificacoesQuery, Result<PaginaDeNotificacoesDto>>
{
    public async Task<Result<PaginaDeNotificacoesDto>> Handle(ListarNotificacoesQuery request, CancellationToken ct) =>
        Result.Success(await notificacoes.ListarDoUsuarioAsync(request.UsuarioId, request.Pagina, request.TamanhoDaPagina, ct));
}
