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
    public void Catalogo_tem_as_15_permissoes_da_1_1_mais_perfis_gerenciar_e_notificacoes_receber()
    {
        Assert.Equal(17, Permissoes.Todas.Count);
        Assert.Contains("notificacoes.receber", Permissoes.Todas); // story 4.2
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
}
