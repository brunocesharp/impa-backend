using Impa.Modules.Financeiro.Domain;

namespace Impa.Modules.Financeiro.Tests;

/// <summary>Story 11-1 — RN-23.2 (campos da conta) e RN-23.3 (parcelas como contas do mesmo parcelamento).</summary>
public class ContaTests
{
    private static readonly Guid Categoria = Guid.CreateVersion7();
    private static readonly DateTimeOffset Agora = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private static DadosDaConta Dados(
        string descricao = "Compra de mochilas", decimal valor = 1000m, FornecedorDaConta? fornecedor = null,
        string? documento = null, string? observacao = null) =>
        new(descricao, Categoria, valor, new DateOnly(2027, 1, 31), fornecedor, documento, observacao);

    [Fact]
    public void Lancar_em_3_parcelas_cria_3_contas_do_mesmo_parcelamento()
    {
        var alfa = new FornecedorDaConta(Guid.CreateVersion7(), "Distribuidora Alfa", "11222333000181");

        var contas = Conta.Lancar(Dados(fornecedor: alfa, documento: " NF 123 ", observacao: " "), 3, Agora);

        Assert.Equal(3, contas.Count);
        Assert.Equal([1, 2, 3], contas.Select(c => c.Parcela));
        Assert.All(contas, c =>
        {
            Assert.Equal(3, c.TotalDeParcelas);
            Assert.Equal("Compra de mochilas", c.Descricao);
            Assert.Equal(Categoria, c.CategoriaId);
            Assert.Equal(alfa.Id, c.FornecedorId);
            Assert.Equal("Distribuidora Alfa", c.FornecedorNome);
            Assert.Equal("11222333000181", c.FornecedorDocumento);
            Assert.Equal("NF 123", c.NumeroDocumento);
            Assert.Null(c.Observacao);
            Assert.Equal(Agora, c.CriadaEm);
        });
        Assert.NotNull(contas[0].ParcelamentoId);
        Assert.All(contas, c => Assert.Equal(contas[0].ParcelamentoId, c.ParcelamentoId));
        Assert.Equal(3, contas.Select(c => c.Id).Distinct().Count());
        Assert.Equal([333.34m, 333.33m, 333.33m], contas.Select(c => c.Valor));
        Assert.Equal(
            [new DateOnly(2027, 1, 31), new DateOnly(2027, 2, 28), new DateOnly(2027, 3, 31)],
            contas.Select(c => c.Vencimento));
    }

    [Fact]
    public void Conta_a_vista_nao_tem_parcelamento_e_o_fornecedor_e_opcional()
    {
        var conta = Assert.Single(Conta.Lancar(Dados(valor: 350m), 1, Agora));

        Assert.Null(conta.ParcelamentoId);
        Assert.Equal((1, 1), (conta.Parcela, conta.TotalDeParcelas));
        Assert.Null(conta.FornecedorId);
        Assert.Null(conta.FornecedorNome);
        Assert.Null(conta.NumeroDocumento);
    }

    [Theory]
    [InlineData("", 100)]
    [InlineData("  ", 100)]
    [InlineData("Aluguel", 0)]
    [InlineData("Aluguel", -1)]
    public void Descricao_e_valor_positivo_sao_obrigatorios(string descricao, decimal valor)
    {
        Assert.Throws<ArgumentException>(() => Conta.Lancar(Dados(descricao, valor), 1, Agora));
    }

    [Fact]
    public void Categoria_e_obrigatoria()
    {
        var semCategoria = Dados() with { CategoriaId = Guid.Empty };

        Assert.Throws<ArgumentException>(() => Conta.Lancar(semCategoria, 1, Agora));
    }

    [Fact]
    public void Alterar_troca_os_campos_e_mantem_a_parcela()
    {
        var segunda = Conta.Lancar(Dados(), 3, Agora)[1];
        var outra = Guid.CreateVersion7();

        segunda.Alterar(new DadosDaConta("Mochilas (2/3)", outra, 340m, new DateOnly(2027, 3, 5), null, "NF 9", "Combinado"));

        Assert.Equal("Mochilas (2/3)", segunda.Descricao);
        Assert.Equal(outra, segunda.CategoriaId);
        Assert.Equal(340m, segunda.Valor);
        Assert.Equal(new DateOnly(2027, 3, 5), segunda.Vencimento);
        Assert.Null(segunda.FornecedorId);
        Assert.Equal("NF 9", segunda.NumeroDocumento);
        Assert.Equal("Combinado", segunda.Observacao);
        Assert.Equal((2, 3), (segunda.Parcela, segunda.TotalDeParcelas));
    }

    [Fact]
    public void Categoria_financeira_inativa_e_reativa()
    {
        var categoria = CategoriaFinanceira.Criar("  Energia ", Agora);
        Assert.Equal("Energia", categoria.Nome);
        Assert.True(categoria.Ativa);

        categoria.Inativar();
        Assert.False(categoria.Ativa);
        categoria.Reativar();
        Assert.True(categoria.Ativa);

        Assert.Throws<ArgumentException>(() => CategoriaFinanceira.Criar(" ", Agora));
    }
}
