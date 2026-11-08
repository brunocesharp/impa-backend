using Ardalis.Result;
using MediatR;

namespace Impa.Modules.Financeiro.Application.Categorias.Queries;

/// <summary>Categorias em ordem de nome; só as ativas, salvo <paramref name="IncluirInativas"/>.</summary>
public sealed record ListarCategoriasQuery(bool IncluirInativas = false) : IRequest<Result<IReadOnlyList<CategoriaDto>>>;

public sealed class ListarCategoriasQueryHandler(ICategoriasRepository categorias)
    : IRequestHandler<ListarCategoriasQuery, Result<IReadOnlyList<CategoriaDto>>>
{
    public async Task<Result<IReadOnlyList<CategoriaDto>>> Handle(ListarCategoriasQuery request, CancellationToken ct) =>
        Result.Success<IReadOnlyList<CategoriaDto>>(
            (await categorias.ListarAsync(request.IncluirInativas, ct)).Select(CategoriaDto.De).ToList());
}
