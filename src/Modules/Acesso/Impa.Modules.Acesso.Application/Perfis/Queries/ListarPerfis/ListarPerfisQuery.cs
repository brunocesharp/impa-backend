using Ardalis.Result;
using MediatR;

namespace Impa.Modules.Acesso.Application.Perfis.Queries.ListarPerfis;

public sealed record ListarPerfisQuery : IRequest<Result<IReadOnlyList<PerfilDto>>>;

public sealed class ListarPerfisQueryHandler(IGestaoPerfis perfis)
    : IRequestHandler<ListarPerfisQuery, Result<IReadOnlyList<PerfilDto>>>
{
    public async Task<Result<IReadOnlyList<PerfilDto>>> Handle(ListarPerfisQuery request, CancellationToken ct) =>
        Result.Success(await perfis.ListarAsync(ct));
}
