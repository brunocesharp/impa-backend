using Ardalis.Result;
using Impa.Modules.Pessoas.Application.Comum;
using MediatR;

namespace Impa.Modules.Pessoas.Application.Clientes.Queries;

/// <summary>
/// Lista da tela Clientes com a busca da RN-14.3 (nome, CPF/CNPJ ou telefone). Por padrão só os ativos;
/// <paramref name="IncluirInativos"/> traz também os inativados (para reativar).
/// </summary>
public sealed record ListarClientesQuery(string? Busca, bool IncluirInativos = false, int? Limite = null)
    : IRequest<Result<IReadOnlyList<ClienteDto>>>
{
    // A busca pode ser um CPF ou telefone (RN-14.6).
    public override string ToString() => $"{nameof(ListarClientesQuery)} {{ *** }}";
}

public sealed record ObterClienteQuery(Guid ClienteId) : IRequest<Result<ClienteDto>>;

public sealed class ListarClientesQueryHandler(IClientesRepository clientes)
    : IRequestHandler<ListarClientesQuery, Result<IReadOnlyList<ClienteDto>>>
{
    public async Task<Result<IReadOnlyList<ClienteDto>>> Handle(ListarClientesQuery request, CancellationToken ct)
    {
        var encontrados = await clientes.BuscarAsync(
            TermosDeBusca.De(request.Busca, request.IncluirInativos, request.Limite), ct);
        return Result.Success<IReadOnlyList<ClienteDto>>(encontrados.Select(ClienteDto.De).ToList());
    }
}

public sealed class ObterClienteQueryHandler(IClientesRepository clientes)
    : IRequestHandler<ObterClienteQuery, Result<ClienteDto>>
{
    public async Task<Result<ClienteDto>> Handle(ObterClienteQuery request, CancellationToken ct) =>
        await clientes.ObterAsync(request.ClienteId, ct) is { } cliente
            ? Result.Success(ClienteDto.De(cliente))
            : Result<ClienteDto>.NotFound(MensagensPessoas.ClienteNaoEncontrado);
}
