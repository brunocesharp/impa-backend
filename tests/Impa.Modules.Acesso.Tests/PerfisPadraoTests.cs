using Impa.Modules.Acesso.Contracts;
using Impa.Modules.Acesso.Application.Perfis;

namespace Impa.Modules.Acesso.Tests;

public class PerfisPadraoTests
{
    [Fact]
    public void Administrador_tem_todas_as_permissoes_e_e_protegido()
    {
        Assert.Equal(
            Permissoes.Todas.OrderBy(p => p),
            PerfisPadrao.Administrador.Permissoes.OrderBy(p => p));
        Assert.True(PerfisPadrao.Administrador.Protegido);
    }

    [Fact]
    public void Operador_de_caixa_tem_so_as_permissoes_do_balcao_da_RN_1_5()
    {
        // PDV, abrir caixa/sangria/suprimento (caixa.operar), fechar caixa, itens sem custo, consultar e cadastrar clientes.
        string[] esperadas =
        [
            Permissoes.PdvVender, Permissoes.CaixaOperar, Permissoes.CaixaFechar,
            Permissoes.CatalogoVer, Permissoes.ClientesVer, Permissoes.ClientesEditar,
        ];

        Assert.Equal(esperadas.OrderBy(p => p), PerfisPadrao.OperadorDeCaixa.Permissoes.OrderBy(p => p));
        Assert.Equal("Operador de caixa", PerfisPadrao.OperadorDeCaixa.Nome);
        Assert.False(PerfisPadrao.OperadorDeCaixa.Protegido);
        Assert.DoesNotContain(Permissoes.CatalogoCustoVer, PerfisPadrao.OperadorDeCaixa.Permissoes);
    }

    [Fact]
    public void Catalogo_tem_as_15_permissoes_da_1_1_mais_perfis_gerenciar_as_duas_da_3_7_e_a_da_11_1()
    {
        Assert.Equal(19, Permissoes.Todas.Count);
        Assert.Contains("configuracao.ver", Permissoes.Todas);
        Assert.Contains("liberar.operacoes", Permissoes.Todas);
        Assert.Contains("perfis.gerenciar", Permissoes.Todas);
    }

    [Fact]
    public void Cada_permissao_do_catalogo_tem_grupo_e_descricao_e_codigo_unico()
    {
        Assert.Equal(Permissoes.Todas.Count, Permissoes.Catalogo.Select(p => p.Codigo).Distinct().Count());
        Assert.All(Permissoes.Catalogo, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Grupo));
            Assert.False(string.IsNullOrWhiteSpace(p.Descricao));
        });
        Assert.True(Permissoes.Existe(Permissoes.CatalogoCustoVer));
        Assert.False(Permissoes.Existe("nao.existe"));
    }

    [Fact]
    public void Story_3_7_clientes_inativar_e_fornecedores_gerenciar_so_para_o_administrador()
    {
        Assert.Equal("clientes.inativar", Permissoes.ClientesInativar);
        Assert.Equal("fornecedores.gerenciar", Permissoes.FornecedoresGerenciar);
        Assert.Contains(Permissoes.ClientesInativar, PerfisPadrao.Administrador.Permissoes);
        Assert.Contains(Permissoes.FornecedoresGerenciar, PerfisPadrao.Administrador.Permissoes);
        Assert.DoesNotContain(Permissoes.ClientesInativar, PerfisPadrao.OperadorDeCaixa.Permissoes);
        Assert.DoesNotContain(Permissoes.FornecedoresGerenciar, PerfisPadrao.OperadorDeCaixa.Permissoes);
        Assert.Contains(Permissoes.ClientesVer, PerfisPadrao.OperadorDeCaixa.Permissoes);
        Assert.Contains(Permissoes.ClientesEditar, PerfisPadrao.OperadorDeCaixa.Permissoes);
    }

    [Fact]
    public void Story_11_1_financeiro_editar_so_para_o_administrador()
    {
        Assert.Equal("financeiro.editar", Permissoes.FinanceiroEditar);
        Assert.Contains(Permissoes.FinanceiroEditar, PerfisPadrao.Administrador.Permissoes);
        Assert.DoesNotContain(Permissoes.FinanceiroEditar, PerfisPadrao.OperadorDeCaixa.Permissoes);
        Assert.Equal("Financeiro", Permissoes.Catalogo.Single(p => p.Codigo == Permissoes.FinanceiroEditar).Grupo);
    }
}
