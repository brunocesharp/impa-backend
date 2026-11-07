using Impa.Modules.Financeiro.Application.Categorias;
using Impa.Modules.Financeiro.Domain;

namespace Impa.Modules.Financeiro.Application.Contas;

/// <summary>Fornecedor da conta na tela; documento com máscara.</summary>
public sealed record FornecedorDaContaDto(Guid Id, string RazaoSocial, string Documento);

/// <summary>Conta (uma parcela) na tela. <c>Vencimento</c> no formato <c>AAAA-MM-DD</c>.</summary>
public sealed record ContaDto(
    Guid Id,
    string Descricao,
    Guid CategoriaId,
    string Categoria,
    decimal Valor,
    DateOnly Vencimento,
    FornecedorDaContaDto? Fornecedor,
    string? NumeroDocumento,
    string? Observacao,
    int Parcela,
    int TotalDeParcelas,
    Guid? ParcelamentoId)
{
    public static ContaDto De(Conta c, string categoria) => new(
        c.Id, c.Descricao, c.CategoriaId, categoria, c.Valor, c.Vencimento,
        c.FornecedorId is { } id
            ? new FornecedorDaContaDto(id, c.FornecedorNome ?? string.Empty, Formatado(c.FornecedorDocumento))
            : null,
        c.NumeroDocumento, c.Observacao, c.Parcela, c.TotalDeParcelas, c.ParcelamentoId);

    public static IReadOnlyList<ContaDto> De(IEnumerable<Conta> contas, IReadOnlyList<CategoriaFinanceira> categorias)
    {
        var nomes = categorias.ToDictionary(c => c.Id, c => c.Nome);
        return contas.Select(c => De(c, nomes.GetValueOrDefault(c.CategoriaId, string.Empty))).ToList();
    }

    private static string Formatado(string? documento) =>
        Impa.Shared.Documento.TentarCriar(documento, out var d, out _) ? d!.Formatado : documento ?? string.Empty;
}

/// <summary>Persistência das contas (schema <c>financeiro</c>).</summary>
public interface IContasRepository
{
    Task<Conta?> ObterAsync(Guid id, CancellationToken ct);

    /// <summary>Em ordem de vencimento.</summary>
    Task<IReadOnlyList<Conta>> ListarAsync(CancellationToken ct);

    /// <summary>Grava as parcelas de um lançamento juntas (tudo ou nada).</summary>
    Task AdicionarAsync(IReadOnlyList<Conta> contas, CancellationToken ct);

    Task SalvarAsync(CancellationToken ct);

    Task RemoverAsync(Conta conta, CancellationToken ct);

    Task<bool> ExisteDaCategoriaAsync(Guid categoriaId, CancellationToken ct);

    Task<bool> ExisteDoFornecedorAsync(Guid fornecedorId, CancellationToken ct);
}

/// <summary>Categorias que o handler já leu (nome para a resposta).</summary>
internal static class CategoriasDasContas
{
    public static async Task<string> NomeAsync(ICategoriasRepository categorias, Guid id, CancellationToken ct) =>
        (await categorias.ObterAsync(id, ct))?.Nome ?? string.Empty;
}
