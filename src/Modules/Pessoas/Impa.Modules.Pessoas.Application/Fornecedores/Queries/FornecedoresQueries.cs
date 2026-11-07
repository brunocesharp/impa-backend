using Ardalis.Result;
using Impa.Modules.Pessoas.Application.Comum;
using MediatR;

namespace Impa.Modules.Pessoas.Application.Fornecedores.Queries;

/// <summary>Lista da tela Fornecedores com a busca da RN-14.3 (razão social, nome fantasia ou CNPJ).</summary>
public sealed record ListarFornecedoresQuery(string? Busca, bool IncluirInativos = false, int? Limite = null)
    : IRequest<Result<IReadOnlyList<FornecedorDto>>>
{
    public override string ToString() => $"{nameof(ListarFornecedoresQuery)} {{ *** }}";
}

public sealed record ObterFornecedorQuery(Guid FornecedorId) : IRequest<Result<FornecedorDto>>;

public sealed class ListarFornecedoresQueryHandler(IFornecedoresRepository fornecedores)
    : IRequestHandler<ListarFornecedoresQuery, Result<IReadOnlyList<FornecedorDto>>>
{
    public async Task<Result<IReadOnlyList<FornecedorDto>>> Handle(ListarFornecedoresQuery request, CancellationToken ct)
    {
        var encontrados = await fornecedores.BuscarAsync(
            TermosDeBusca.De(request.Busca, request.IncluirInativos, request.Limite), ct);
        return Result.Success<IReadOnlyList<FornecedorDto>>(encontrados.Select(FornecedorDto.De).ToList());
    }
}

public sealed class ObterFornecedorQueryHandler(IFornecedoresRepository fornecedores)
    : IRequestHandler<ObterFornecedorQuery, Result<FornecedorDto>>
{
    public async Task<Result<FornecedorDto>> Handle(ObterFornecedorQuery request, CancellationToken ct) =>
        await fornecedores.ObterAsync(request.FornecedorId, ct) is { } fornecedor
            ? Result.Success(FornecedorDto.De(fornecedor))
            : Result<FornecedorDto>.NotFound(MensagensPessoas.FornecedorNaoEncontrado);
}
