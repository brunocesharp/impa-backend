namespace Impa.Modules.Financeiro.Domain;

/// <summary>
/// Fornecedor da conta, como estava no cadastro do módulo Pessoas ao lançar ou alterar (razão social e documento só
/// com dígitos). O Financeiro guarda o id para a porta de histórico (RN-14.4) e os dados para a lista.
/// </summary>
public sealed record FornecedorDaConta(Guid Id, string Nome, string Documento);

/// <summary>
/// Campos da conta (RN-23.2): descrição, categoria, valor (&gt; 0) e vencimento obrigatórios; fornecedor, número do
/// documento e observação opcionais. No lançamento, <see cref="Valor"/> é o total e <see cref="Vencimento"/> o primeiro.
/// </summary>
public sealed record DadosDaConta(
    string Descricao,
    Guid CategoriaId,
    decimal Valor,
    DateOnly Vencimento,
    FornecedorDaConta? Fornecedor,
    string? NumeroDocumento,
    string? Observacao);

/// <summary>
/// Conta a pagar. Uma conta parcelada vira uma conta por parcela ("2/3"), ligadas pelo <see cref="ParcelamentoId"/>
/// (RN-23.3). Pagamento e estorno chegam na story 11.2.
/// </summary>
public sealed class Conta
{
    public const int DescricaoMaximo = 150;
    public const int NumeroDocumentoMaximo = 60;
    public const int ObservacaoMaximo = 1000;
    public const int FornecedorNomeMaximo = 150;
    public const int FornecedorDocumentoMaximo = 14;

    private Conta()
    {
        Descricao = string.Empty;
    }

    public Guid Id { get; private set; }

    public string Descricao { get; private set; }

    public Guid CategoriaId { get; private set; }

    public decimal Valor { get; private set; }

    public DateOnly Vencimento { get; private set; }

    public Guid? FornecedorId { get; private set; }

    public string? FornecedorNome { get; private set; }

    /// <summary>CNPJ ou CPF do fornecedor, só os dígitos.</summary>
    public string? FornecedorDocumento { get; private set; }

    public string? NumeroDocumento { get; private set; }

    public string? Observacao { get; private set; }

    /// <summary>Número da parcela (1 a <see cref="TotalDeParcelas"/>).</summary>
    public int Parcela { get; private set; }

    public int TotalDeParcelas { get; private set; }

    /// <summary>Mesmo valor nas parcelas de um lançamento parcelado; nulo na conta de parcela única.</summary>
    public Guid? ParcelamentoId { get; private set; }

    public DateTimeOffset CriadaEm { get; private set; }

    /// <summary>Lança a conta em <paramref name="parcelas"/> parcelas (RN-23.3); uma parcela = uma conta.</summary>
    public static IReadOnlyList<Conta> Lancar(DadosDaConta dados, int parcelas, DateTimeOffset agora)
    {
        Validar(dados);
        var calculadas = Parcelamento.Calcular(dados.Valor, parcelas, dados.Vencimento);
        Guid? parcelamento = parcelas > 1 ? Guid.CreateVersion7() : null;

        return calculadas.Select(p =>
        {
            var conta = new Conta
            {
                Id = Guid.CreateVersion7(),
                Parcela = p.Numero,
                TotalDeParcelas = p.Total,
                ParcelamentoId = parcelamento,
                CriadaEm = agora,
            };
            conta.Aplicar(dados with { Valor = p.Valor, Vencimento = p.Vencimento });
            return conta;
        }).ToList();
    }

    /// <summary>Altera a parcela (sem pagamento, RN-23.7); o número da parcela não muda.</summary>
    public void Alterar(DadosDaConta dados)
    {
        Validar(dados);
        Aplicar(dados);
    }

    private static void Validar(DadosDaConta dados)
    {
        if (string.IsNullOrWhiteSpace(dados.Descricao))
            throw new ArgumentException("A descrição da conta é obrigatória", nameof(dados));
        if (dados.CategoriaId == Guid.Empty)
            throw new ArgumentException("A categoria da conta é obrigatória", nameof(dados));
        Valores.ExigirValor(dados.Valor);
    }

    private void Aplicar(DadosDaConta dados)
    {
        Descricao = dados.Descricao.Trim();
        CategoriaId = dados.CategoriaId;
        Valor = dados.Valor;
        Vencimento = dados.Vencimento;
        FornecedorId = dados.Fornecedor?.Id;
        FornecedorNome = dados.Fornecedor?.Nome;
        FornecedorDocumento = dados.Fornecedor?.Documento;
        NumeroDocumento = Valores.Opcional(dados.NumeroDocumento);
        Observacao = Valores.Opcional(dados.Observacao);
    }
}
