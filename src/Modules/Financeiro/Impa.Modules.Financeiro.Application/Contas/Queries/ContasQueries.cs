using Ardalis.Result;
using Impa.Modules.Financeiro.Application.Categorias;
using MediatR;

namespace Impa.Modules.Financeiro.Application.Contas.Queries;

/// <summary>Contas (parcelas) em ordem de vencimento. Filtros, situação e totais chegam na story 11.3.</summary>
public sealed record ListarContasQuery : IRequest<Result<IReadOnlyList<ContaDto>>>;

public sealed record ObterContaQuery(Guid ContaId) : IRequest<Result<ContaDto>>;

public sealed class ListarContasQueryHandler(IContasRepository contas, ICategoriasRepository categorias)
    : IRequestHandler<ListarContasQuery, Result<IReadOnlyList<ContaDto>>>
{
    public async Task<Result<IReadOnlyList<ContaDto>>> Handle(ListarContasQuery request, CancellationToken ct)
    {
        var lista = await contas.ListarAsync(ct);
        return Result.Success(ContaDto.De(lista, await categorias.ListarAsync(incluirInativas: true, ct)));
    }
}

public sealed class ObterContaQueryHandler(IContasRepository contas, ICategoriasRepository categorias)
    : IRequestHandler<ObterContaQuery, Result<ContaDto>>
{
    public async Task<Result<ContaDto>> Handle(ObterContaQuery request, CancellationToken ct) =>
        await contas.ObterAsync(request.ContaId, ct) is { } conta
            ? Result.Success(ContaDto.De(conta, await CategoriasDasContas.NomeAsync(categorias, conta.CategoriaId, ct)))
            : Result<ContaDto>.NotFound(MensagensFinanceiro.ContaNaoEncontrada);
}
