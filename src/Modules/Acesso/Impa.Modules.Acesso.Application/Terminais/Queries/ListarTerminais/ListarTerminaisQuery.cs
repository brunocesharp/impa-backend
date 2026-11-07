using Ardalis.Result;
using MediatR;

namespace Impa.Modules.Acesso.Application.Terminais.Queries.ListarTerminais;

/// <summary>Terminais ativos e revogados. <paramref name="TokenAtual"/>: cookie de terminal deste dispositivo, se houver.</summary>
public sealed record ListarTerminaisQuery(string? TokenAtual) : IRequest<Result<IReadOnlyList<TerminalDto>>>
{
    public override string ToString() => $"{nameof(ListarTerminaisQuery)} {{ TokenAtual = *** }}";
}

public sealed class ListarTerminaisQueryHandler(ITerminais terminais)
    : IRequestHandler<ListarTerminaisQuery, Result<IReadOnlyList<TerminalDto>>>
{
    public async Task<Result<IReadOnlyList<TerminalDto>>> Handle(ListarTerminaisQuery request, CancellationToken ct) =>
        Result.Success(await terminais.ListarAsync(TokenDoTerminal.HashSeValido(request.TokenAtual), ct));
}
