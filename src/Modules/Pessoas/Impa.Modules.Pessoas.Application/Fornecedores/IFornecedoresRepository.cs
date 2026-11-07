using Impa.Modules.Pessoas.Application.Comum;
using Impa.Modules.Pessoas.Domain;

namespace Impa.Modules.Pessoas.Application.Fornecedores;

/// <summary>Fornecedor na tela. Documento com máscara; telefone só dígitos.</summary>
public sealed record FornecedorDto(
    Guid Id,
    string RazaoSocial,
    string? NomeFantasia,
    string Documento,
    string? Telefone,
    string? Email,
    string? Contato,
    bool Ativo)
{
    public static FornecedorDto De(Fornecedor f) => new(f.Id, f.RazaoSocial, f.NomeFantasia,
        Impa.Shared.Documento.Criar(f.Documento).Formatado, f.Telefone, f.Email, f.Contato, f.Ativo);

    // RN-14.6: fornecedor pessoa física tem CPF; nada de dados de contato em log.
    public override string ToString() => $"{nameof(FornecedorDto)} {{ Id = {Id} }}";
}

/// <summary>Persistência dos fornecedores (schema <c>pessoas</c>).</summary>
public interface IFornecedoresRepository
{
    Task<Fornecedor?> ObterAsync(Guid id, CancellationToken ct);

    /// <summary>Outro fornecedor com o mesmo documento (só dígitos), ativo ou não; ignora <paramref name="exceto"/>.</summary>
    Task<Fornecedor?> ObterPorDocumentoAsync(string documento, Guid? exceto, CancellationToken ct);

    Task<IReadOnlyList<Fornecedor>> BuscarAsync(TermosDeBusca termos, CancellationToken ct);

    /// <summary>Falso quando o índice único do documento recusou (cadastro simultâneo).</summary>
    Task<bool> AdicionarAsync(Fornecedor fornecedor, CancellationToken ct);

    /// <summary>Falso quando o índice único do documento recusou.</summary>
    Task<bool> SalvarAsync(CancellationToken ct);

    Task RemoverAsync(Fornecedor fornecedor, CancellationToken ct);
}
