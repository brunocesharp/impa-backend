using Ardalis.Result;
using MediatR;

namespace Impa.Modules.Financeiro.Application.FormasDePagamento.Queries;

/// <summary>Formas em ordem de nome; só as ativas, salvo <paramref name="IncluirInativas"/>.</summary>
public sealed record ListarFormasDePagamentoQuery(bool IncluirInativas = false) : IRequest<Result<IReadOnlyList<FormaDePagamentoDto>>>;

public sealed class ListarFormasDePagamentoQueryHandler(IFormasDePagamentoRepository formas)
    : IRequestHandler<ListarFormasDePagamentoQuery, Result<IReadOnlyList<FormaDePagamentoDto>>>
{
    public async Task<Result<IReadOnlyList<FormaDePagamentoDto>>> Handle(ListarFormasDePagamentoQuery request, CancellationToken ct) =>
        Result.Success<IReadOnlyList<FormaDePagamentoDto>>(
            (await formas.ListarAsync(request.IncluirInativas, ct)).Select(FormaDePagamentoDto.De).ToList());
}
