using Impa.Modules.Financeiro.Domain;

namespace Impa.Modules.Financeiro.Tests;

/// <summary>Story 11-1 — RN-23.3 (parcelamento) e o cenário 23.1.</summary>
public class ParcelamentoTests
{
    [Fact]
    public void Cenario_23_1_mil_reais_em_3_parcelas_com_a_diferenca_na_primeira_e_o_ultimo_dia_do_mes()
    {
        var parcelas = Parcelamento.Calcular(1000.00m, 3, new DateOnly(2027, 1, 31));

        Assert.Equal(
        [
            new ParcelaCalculada(1, 3, 333.34m, new DateOnly(2027, 1, 31)),
            new ParcelaCalculada(2, 3, 333.33m, new DateOnly(2027, 2, 28)),
            new ParcelaCalculada(3, 3, 333.33m, new DateOnly(2027, 3, 31)),
        ], parcelas);
        Assert.Equal(1000.00m, parcelas.Sum(p => p.Valor));
    }

    [Fact]
    public void Vencimentos_seguem_o_dia_do_primeiro_e_nao_o_do_anterior()
    {
        var parcelas = Parcelamento.Calcular(500m, 5, new DateOnly(2027, 1, 30));

        Assert.Equal(
        [
            new DateOnly(2027, 1, 30), new DateOnly(2027, 2, 28), new DateOnly(2027, 3, 30),
            new DateOnly(2027, 4, 30), new DateOnly(2027, 5, 30),
        ], parcelas.Select(p => p.Vencimento));
    }

    [Fact]
    public void Ano_bissexto_vence_em_29_de_fevereiro()
    {
        var parcelas = Parcelamento.Calcular(200m, 2, new DateOnly(2028, 1, 31));

        Assert.Equal(new DateOnly(2028, 2, 29), parcelas[1].Vencimento);
    }

    [Fact]
    public void Parcelas_que_viram_o_ano()
    {
        var parcelas = Parcelamento.Calcular(300m, 3, new DateOnly(2026, 11, 15));

        Assert.Equal(
            [new DateOnly(2026, 11, 15), new DateOnly(2026, 12, 15), new DateOnly(2027, 1, 15)],
            parcelas.Select(p => p.Vencimento));
    }

    [Fact]
    public void Divisao_exata_tem_parcelas_iguais()
    {
        Assert.All(Parcelamento.Calcular(900m, 3, new DateOnly(2027, 1, 10)), p => Assert.Equal(300m, p.Valor));
    }

    [Theory]
    [InlineData(100.00, 7, 14.32, 14.28)]
    [InlineData(0.05, 3, 0.03, 0.01)]
    [InlineData(10.00, 3, 3.34, 3.33)]
    public void Diferenca_de_centavos_fica_na_primeira(decimal valor, int quantidade, decimal primeira, decimal demais)
    {
        var parcelas = Parcelamento.Calcular(valor, quantidade, new DateOnly(2027, 1, 1));

        Assert.Equal(primeira, parcelas[0].Valor);
        Assert.All(parcelas.Skip(1), p => Assert.Equal(demais, p.Valor));
        Assert.Equal(valor, parcelas.Sum(p => p.Valor));
    }

    [Fact]
    public void Uma_parcela_e_a_propria_conta()
    {
        var parcela = Assert.Single(Parcelamento.Calcular(150.75m, 1, new DateOnly(2027, 1, 31)));

        Assert.Equal(new ParcelaCalculada(1, 1, 150.75m, new DateOnly(2027, 1, 31)), parcela);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-10, 1)]
    [InlineData(10.001, 1)]
    [InlineData(100, 0)]
    [InlineData(100, 121)]
    [InlineData(0.02, 3)]
    public void Valor_ou_quantidade_invalidos_sao_recusados(decimal valor, int quantidade)
    {
        Assert.Throws<ArgumentException>(() => Parcelamento.Calcular(valor, quantidade, new DateOnly(2027, 1, 1)));
    }
}
