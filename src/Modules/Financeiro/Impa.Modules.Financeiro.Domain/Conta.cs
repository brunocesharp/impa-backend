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
/// (RN-23.3). Os pagamentos são as <see cref="Baixas"/> (RN-23.5): <see cref="ValorPago"/> soma as baixas não estornadas
/// e fica gravado na conta (com a versão da linha, dois pagamentos ao mesmo tempo não passam do saldo). Com pagamento,
/// só a descrição e a observação podem mudar (RN-23.7).
/// </summary>
public sealed class Conta
{
    public const int DescricaoMaximo = 150;
    public const int NumeroDocumentoMaximo = 60;
    public const int ObservacaoMaximo = 1000;
    public const int FornecedorNomeMaximo = 150;
    public const int FornecedorDocumentoMaximo = 14;

    /// <summary>O maior valor que cabe na coluna <c>numeric(14,2)</c>.</summary>
    public const decimal ValorMaximo = 999_999_999_999.99m;

    /// <summary>Faixa aceita para o vencimento (barra erro de digitação e o estouro de datas).</summary>
    public const int AnoMinimo = 2000;
    public const int AnoMaximo = 2100;

    public static bool VencimentoNaFaixa(DateOnly vencimento) => vencimento.Year is >= AnoMinimo and <= AnoMaximo;

    private readonly List<Baixa> _baixas = [];

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

    /// <summary>Soma das baixas não estornadas.</summary>
    public decimal ValorPago { get; private set; }

    public decimal Saldo => Valor - ValorPago;

    /// <summary>RN-23.7: a conta tem pagamento quando tem baixa não estornada.</summary>
    public bool TemPagamento => ValorPago > 0;

    public SituacaoDoPagamento Pagamento => ValorPago == 0
        ? SituacaoDoPagamento.SemPagamento
        : Saldo == 0 ? SituacaoDoPagamento.Paga : SituacaoDoPagamento.ParcialmentePaga;

    /// <summary>Todas as baixas, estornadas ou não, na ordem em que foram registradas.</summary>
    public IReadOnlyList<Baixa> Baixas => _baixas;

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

    /// <summary>
    /// Altera a parcela; o número da parcela não muda. Com pagamento (RN-23.7), só a descrição e a observação podem
    /// mudar: categoria, valor, vencimento, fornecedor e número do documento precisam vir iguais.
    /// </summary>
    public void Alterar(DadosDaConta dados)
    {
        Validar(dados);
        if (TemPagamento && CamposTravadosAlterados(dados).Count > 0)
            throw new InvalidOperationException("Conta com pagamento: só a descrição e a observação podem ser alteradas");
        Aplicar(dados);
    }

    /// <summary>
    /// Campos que a RN-23.7 trava numa conta com pagamento e que <paramref name="dados"/> muda (nomes de
    /// <see cref="DadosDaConta"/>; o fornecedor é comparado pelo documento).
    /// </summary>
    public IReadOnlyList<string> CamposTravadosAlterados(DadosDaConta dados)
    {
        var campos = new List<string>();
        if (dados.CategoriaId != CategoriaId)
            campos.Add(nameof(DadosDaConta.CategoriaId));
        if (dados.Valor != Valor)
            campos.Add(nameof(DadosDaConta.Valor));
        if (dados.Vencimento != Vencimento)
            campos.Add(nameof(DadosDaConta.Vencimento));
        if (dados.Fornecedor?.Documento != FornecedorDocumento)
            campos.Add(nameof(DadosDaConta.Fornecedor));
        if (Valores.Opcional(dados.NumeroDocumento) != NumeroDocumento)
            campos.Add(nameof(DadosDaConta.NumeroDocumento));
        return campos;
    }

    /// <summary>
    /// Registra uma baixa (RN-23.5): valor maior que zero e no máximo o saldo; menor que o saldo deixa a conta
    /// parcialmente paga.
    /// </summary>
    public Baixa Pagar(DateOnly data, decimal valor, Guid formaDePagamentoId, DateTimeOffset agora)
    {
        if (!VencimentoNaFaixa(data))
            throw new ArgumentException($"A data do pagamento precisa estar entre {AnoMinimo} e {AnoMaximo}", nameof(data));
        if (formaDePagamentoId == Guid.Empty)
            throw new ArgumentException("A forma de pagamento é obrigatória", nameof(formaDePagamentoId));
        Valores.ExigirValor(valor);
        if (valor > Saldo)
            throw new ArgumentException(ValorMaiorQueOSaldo(Saldo), nameof(valor));

        var baixa = Baixa.Registrar(Id, data, valor, formaDePagamentoId, agora);
        _baixas.Add(baixa);
        ValorPago += valor;
        return baixa;
    }

    /// <summary>Estorna uma baixa, com motivo (RN-23.7): ela fica registrada como estornada e o saldo volta.</summary>
    public Baixa Estornar(Guid baixaId, string motivo, UsuarioDoEstorno usuario, DateTimeOffset agora)
    {
        var baixa = _baixas.FirstOrDefault(b => b.Id == baixaId)
                    ?? throw new ArgumentException("Pagamento não encontrado nesta conta", nameof(baixaId));
        baixa.Estornar(motivo, usuario, agora);
        ValorPago -= baixa.Valor;
        return baixa;
    }

    /// <summary>Mensagem do cenário 23.2: "Valor maior que o saldo da conta (R$ 500,00)".</summary>
    public static string ValorMaiorQueOSaldo(decimal saldo) => $"Valor maior que o saldo da conta ({Dinheiro.Formatar(saldo)})";

    private static void Validar(DadosDaConta dados)
    {
        if (string.IsNullOrWhiteSpace(dados.Descricao))
            throw new ArgumentException("A descrição da conta é obrigatória", nameof(dados));
        if (dados.CategoriaId == Guid.Empty)
            throw new ArgumentException("A categoria da conta é obrigatória", nameof(dados));
        Valores.ExigirValor(dados.Valor);
        if (!VencimentoNaFaixa(dados.Vencimento))
            throw new ArgumentException($"O vencimento precisa estar entre {AnoMinimo} e {AnoMaximo}", nameof(dados));
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
