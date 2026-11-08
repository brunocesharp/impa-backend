using Impa.Modules.Financeiro.Domain;

namespace Impa.Modules.Financeiro.Tests;

/// <summary>Story 11-2 — baixa (RN-23.5, cenário 23.2), estorno (cenário 23.5) e a trava da RN-23.7.</summary>
public class BaixaTests
{
    private static readonly Guid Categoria = Guid.CreateVersion7();
    private static readonly Guid Pix = Guid.CreateVersion7();
    private static readonly DateTimeOffset Agora = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Dia5 = new(2026, 11, 5);
    private static readonly UsuarioDoEstorno Admin = new(Guid.CreateVersion7(), "admin");

    private static DadosDaConta Dados(decimal valor = 500m, string descricao = "Aluguel novembro") =>
        new(descricao, Categoria, valor, new DateOnly(2026, 11, 10), null, "Recibo 11", "Combinado");

    private static Conta ContaDe(decimal valor = 500m) => Assert.Single(Conta.Lancar(Dados(valor), 1, Agora));

    // ---- Cenário 23.2 (CA-1, CA-4) ----

    [Fact]
    public void Conta_nova_esta_sem_pagamento_com_saldo_igual_ao_valor()
    {
        var conta = ContaDe();

        Assert.Equal((500m, 0m, SituacaoDoPagamento.SemPagamento, false), (conta.Saldo, conta.ValorPago, conta.Pagamento, conta.TemPagamento));
        Assert.Empty(conta.Baixas);
    }

    [Fact]
    public void Cenario_23_2_pagar_500_deixa_a_conta_paga()
    {
        var conta = ContaDe();

        var baixa = conta.Pagar(Dia5, 500m, Pix, Agora);

        Assert.Equal((0m, 500m, SituacaoDoPagamento.Paga), (conta.Saldo, conta.ValorPago, conta.Pagamento));
        Assert.Equal((Dia5, 500m, Pix, conta.Id, Agora, false), (baixa.Data, baixa.Valor, baixa.FormaDePagamentoId, baixa.ContaId, baixa.RegistradaEm, baixa.Estornada));
        Assert.Same(baixa, Assert.Single(conta.Baixas));
    }

    [Fact]
    public void Cenario_23_2_pagar_200_deixa_a_conta_parcialmente_paga_com_saldo_300()
    {
        var conta = ContaDe();

        conta.Pagar(Dia5, 200m, Pix, Agora);

        Assert.Equal((300m, SituacaoDoPagamento.ParcialmentePaga, true), (conta.Saldo, conta.Pagamento, conta.TemPagamento));
    }

    [Fact]
    public void Cenario_23_2_pagar_600_e_recusado_com_o_saldo_na_mensagem()
    {
        var conta = ContaDe();

        var erro = Assert.Throws<ArgumentException>(() => conta.Pagar(Dia5, 600m, Pix, Agora));

        Assert.StartsWith("Valor maior que o saldo da conta (R$ 500,00)", erro.Message);
        Assert.Empty(conta.Baixas);
        Assert.Equal(500m, conta.Saldo);
    }

    [Fact]
    public void Pagamentos_parciais_somam_ate_quitar_e_depois_nada_cabe()
    {
        var conta = ContaDe(1000m);

        conta.Pagar(Dia5, 333.33m, Pix, Agora);
        conta.Pagar(Dia5, 666.67m, Pix, Agora);

        Assert.Equal((0m, SituacaoDoPagamento.Paga), (conta.Saldo, conta.Pagamento));
        Assert.StartsWith("Valor maior que o saldo da conta (R$ 0,00)",
            Assert.Throws<ArgumentException>(() => conta.Pagar(Dia5, 0.01m, Pix, Agora)).Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.001)]
    public void Valor_da_baixa_precisa_ser_positivo_com_dois_decimais(decimal valor)
    {
        Assert.Throws<ArgumentException>(() => ContaDe().Pagar(Dia5, valor, Pix, Agora));
    }

    [Fact]
    public void Forma_e_data_sao_obrigatorias()
    {
        Assert.Throws<ArgumentException>(() => ContaDe().Pagar(Dia5, 10m, Guid.Empty, Agora));
        Assert.Throws<ArgumentException>(() => ContaDe().Pagar(new DateOnly(1999, 12, 31), 10m, Pix, Agora));
    }

    [Theory]
    [InlineData(500, "R$ 500,00")]
    [InlineData(1234567.8, "R$ 1.234.567,80")]
    [InlineData(0, "R$ 0,00")]
    [InlineData(0.05, "R$ 0,05")]
    public void Dinheiro_no_formato_brasileiro(decimal valor, string esperado)
    {
        Assert.Equal(esperado, Dinheiro.Formatar(valor));
    }

    // ---- Cenário 23.5 (CA-2, CA-5) ----

    [Fact]
    public void Cenario_23_5_estornar_devolve_o_saldo_e_guarda_o_estorno()
    {
        var conta = Assert.Single(Conta.Lancar(Dados(350m, "Energia outubro"), 1, Agora));
        var baixa = conta.Pagar(Dia5, 350m, Pix, Agora);
        var depois = Agora.AddHours(2);

        var estornada = conta.Estornar(baixa.Id, "  Pagamento lançado na conta errada ", Admin, depois);

        Assert.Same(baixa, estornada);
        Assert.Equal((350m, 0m, SituacaoDoPagamento.SemPagamento, false), (conta.Saldo, conta.ValorPago, conta.Pagamento, conta.TemPagamento));
        Assert.Same(baixa, Assert.Single(conta.Baixas)); // a baixa não é apagada
        Assert.True(baixa.Estornada);
        Assert.Equal((depois, Admin.Id, "admin", "Pagamento lançado na conta errada"),
            (baixa.EstornadaEm, baixa.EstornadaPorId, baixa.EstornadaPorNome, baixa.MotivoDoEstorno));
    }

    [Fact]
    public void Estorno_exige_motivo_e_nao_se_repete()
    {
        var conta = ContaDe();
        var baixa = conta.Pagar(Dia5, 200m, Pix, Agora);

        Assert.Throws<ArgumentException>(() => conta.Estornar(baixa.Id, " ", Admin, Agora));
        conta.Estornar(baixa.Id, "Duplicado", Admin, Agora);
        Assert.Throws<InvalidOperationException>(() => conta.Estornar(baixa.Id, "De novo", Admin, Agora));
        Assert.Throws<ArgumentException>(() => conta.Estornar(Guid.CreateVersion7(), "Outra", Admin, Agora));
        Assert.Equal(500m, conta.Saldo);
    }

    [Fact]
    public void Estornar_uma_de_duas_baixas_deixa_a_conta_parcialmente_paga()
    {
        var conta = ContaDe();
        var primeira = conta.Pagar(Dia5, 200m, Pix, Agora);
        conta.Pagar(Dia5, 300m, Pix, Agora);

        conta.Estornar(primeira.Id, "Valor errado", Admin, Agora);

        Assert.Equal((200m, SituacaoDoPagamento.ParcialmentePaga), (conta.Saldo, conta.Pagamento));
    }

    // ---- RN-23.7 (CA-5) ----

    [Fact]
    public void Conta_com_pagamento_so_altera_descricao_e_observacao()
    {
        var conta = ContaDe();
        conta.Pagar(Dia5, 100m, Pix, Agora);

        conta.Alterar(Dados() with { Descricao = "Aluguel nov/26", Observacao = "Pago em parte" });
        Assert.Equal(("Aluguel nov/26", "Pago em parte"), (conta.Descricao, conta.Observacao));

        Assert.Throws<InvalidOperationException>(() => conta.Alterar(Dados() with { Valor = 600m }));
        Assert.Throws<InvalidOperationException>(() => conta.Alterar(Dados() with { Vencimento = new DateOnly(2026, 11, 20) }));
        Assert.Throws<InvalidOperationException>(() => conta.Alterar(Dados() with { CategoriaId = Guid.CreateVersion7() }));
        Assert.Throws<InvalidOperationException>(() => conta.Alterar(Dados() with { NumeroDocumento = "Recibo 12" }));
        Assert.Throws<InvalidOperationException>(() => conta.Alterar(Dados() with
        {
            Fornecedor = new FornecedorDaConta(Guid.CreateVersion7(), "Imobiliária", "11222333000181"),
        }));
        Assert.Equal(500m, conta.Valor);
    }

    [Fact]
    public void Depois_do_estorno_a_conta_volta_a_ser_editavel()
    {
        var conta = ContaDe();
        var baixa = conta.Pagar(Dia5, 100m, Pix, Agora);
        conta.Estornar(baixa.Id, "Errado", Admin, Agora);

        conta.Alterar(Dados() with { Valor = 550m });

        Assert.Equal((550m, 550m), (conta.Valor, conta.Saldo));
    }

    // ---- RN-23.4 (CA-3) ----

    [Fact]
    public void Forma_de_pagamento_financeira_inativa_e_reativa()
    {
        var forma = FormaDePagamentoFinanceira.Criar("  Pix ", Agora);
        Assert.Equal(("Pix", true), (forma.Nome, forma.Ativa));

        forma.Inativar();
        Assert.False(forma.Ativa);
        forma.Reativar();
        forma.Renomear("PIX");
        Assert.Equal(("PIX", true), (forma.Nome, forma.Ativa));

        Assert.Throws<ArgumentException>(() => FormaDePagamentoFinanceira.Criar(" ", Agora));
    }
}
