using Impa.Modules.Financeiro.Application.Categorias;
using Impa.Modules.Financeiro.Domain;

namespace Impa.Modules.Financeiro.Application.Contas;

/// <summary>Fornecedor da conta na tela; documento com máscara.</summary>
public sealed record FornecedorDaContaDto(Guid Id, string RazaoSocial, string Documento);

/// <summary>
/// Baixa (pagamento) na tela (RN-23.5). Estornada: <c>EstornadaEm</c>, <c>EstornadaPor</c> (login) e
/// <c>MotivoDoEstorno</c> preenchidos.
/// </summary>
public sealed record BaixaDto(
    Guid Id,
    DateOnly Data,
    decimal Valor,
    Guid FormaDePagamentoId,
    string FormaDePagamento,
    DateTimeOffset RegistradaEm,
    bool Estornada,
    DateTimeOffset? EstornadaEm,
    string? EstornadaPor,
    string? MotivoDoEstorno)
{
    public static BaixaDto De(Baixa b, IReadOnlyDictionary<Guid, string> formas) => new(
        b.Id, b.Data, b.Valor, b.FormaDePagamentoId, formas.GetValueOrDefault(b.FormaDePagamentoId, string.Empty),
        b.RegistradaEm, b.Estornada, b.EstornadaEm, b.EstornadaPorNome, b.MotivoDoEstorno);
}

/// <summary>
/// Conta (uma parcela) na tela. <c>Vencimento</c> no formato <c>AAAA-MM-DD</c>. <c>SituacaoDoPagamento</c>:
/// <c>sem-pagamento</c>, <c>parcialmente-paga</c> ou <c>paga</c> (RN-23.5); <c>Saldo</c> = valor − baixas não
/// estornadas; <c>Baixas</c> em ordem de registro, com as estornadas.
/// </summary>
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
    Guid? ParcelamentoId,
    decimal ValorPago,
    decimal Saldo,
    string SituacaoDoPagamento,
    IReadOnlyList<BaixaDto> Baixas)
{
    private static readonly IReadOnlyDictionary<Guid, string> SemFormas = new Dictionary<Guid, string>();

    /// <param name="formas">Nomes das formas de pagamento das baixas (pode faltar na conta sem baixas).</param>
    public static ContaDto De(Conta c, string categoria, IReadOnlyDictionary<Guid, string>? formas = null) => new(
        c.Id, c.Descricao, c.CategoriaId, categoria, c.Valor, c.Vencimento,
        c.FornecedorId is { } id
            ? new FornecedorDaContaDto(id, c.FornecedorNome ?? string.Empty, Formatado(c.FornecedorDocumento))
            : null,
        c.NumeroDocumento, c.Observacao, c.Parcela, c.TotalDeParcelas, c.ParcelamentoId,
        c.ValorPago, c.Saldo, Situacao(c.Pagamento),
        c.Baixas.OrderBy(b => b.RegistradaEm).ThenBy(b => b.Id).Select(b => BaixaDto.De(b, formas ?? SemFormas)).ToList());

    public static IReadOnlyList<ContaDto> De(
        IEnumerable<Conta> contas, IReadOnlyList<CategoriaFinanceira> categorias, IReadOnlyDictionary<Guid, string> formas)
    {
        var nomes = categorias.ToDictionary(c => c.Id, c => c.Nome);
        return contas.Select(c => De(c, nomes.GetValueOrDefault(c.CategoriaId, string.Empty), formas)).ToList();
    }

    public static string Situacao(SituacaoDoPagamento situacao) => situacao switch
    {
        Domain.SituacaoDoPagamento.Paga => "paga",
        Domain.SituacaoDoPagamento.ParcialmentePaga => "parcialmente-paga",
        _ => "sem-pagamento",
    };

    private static string Formatado(string? documento) =>
        Impa.Shared.Documento.TentarCriar(documento, out var d, out _) ? d!.Formatado : documento ?? string.Empty;
}

/// <summary>Persistência das contas (schema <c>financeiro</c>).</summary>
public interface IContasRepository
{
    /// <summary>A conta com as baixas.</summary>
    Task<Conta?> ObterAsync(Guid id, CancellationToken ct);

    /// <summary>A conta (com as baixas) que tem a baixa <paramref name="baixaId"/>.</summary>
    Task<Conta?> ObterPelaBaixaAsync(Guid baixaId, CancellationToken ct);

    /// <summary>Em ordem de vencimento, com as baixas.</summary>
    Task<IReadOnlyList<Conta>> ListarAsync(CancellationToken ct);

    /// <summary>Grava as parcelas de um lançamento juntas (tudo ou nada).</summary>
    Task AdicionarAsync(IReadOnlyList<Conta> contas, CancellationToken ct);

    /// <summary>
    /// Grava as mudanças da conta. Nada é gravado quando outra operação mudou a conta depois da leitura (versão da
    /// linha), quando a forma de pagamento de uma baixa nova foi excluída entre a validação e a gravação (FK) ou quando a
    /// conta de uma baixa nova foi excluída depois da leitura (FK).
    /// </summary>
    Task<GravacaoDaConta> SalvarAsync(CancellationToken ct);

    Task<bool> ExisteBaixaDaFormaAsync(Guid formaDePagamentoId, CancellationToken ct);

    /// <summary>Falso quando outra operação mudou a conta depois da leitura (versão da linha); nada é excluído.</summary>
    Task<bool> RemoverAsync(Conta conta, CancellationToken ct);

    Task<bool> ExisteDaCategoriaAsync(Guid categoriaId, CancellationToken ct);

    Task<bool> ExisteDoFornecedorAsync(Guid fornecedorId, CancellationToken ct);
}

/// <summary>Resultado de <see cref="IContasRepository.SalvarAsync"/>.</summary>
public enum GravacaoDaConta
{
    Gravada,

    /// <summary>A versão da linha mudou desde a leitura (409 "A conta foi alterada em outra operação…").</summary>
    AlteradaAoMesmoTempo,

    /// <summary>A FK baixas → formas_pagamento recusou (23503): a forma foi excluída depois da validação.</summary>
    FormaDePagamentoNaoEncontrada,

    /// <summary>A FK baixas → contas recusou (23503): a conta foi excluída depois da leitura (404 "Conta não encontrada").</summary>
    ContaNaoEncontrada,
}

/// <summary>Categorias que o handler já leu (nome para a resposta).</summary>
internal static class CategoriasDasContas
{
    public static async Task<string> NomeAsync(ICategoriasRepository categorias, Guid id, CancellationToken ct) =>
        (await categorias.ObterAsync(id, ct))?.Nome ?? string.Empty;
}
