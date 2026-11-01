using Impa.Modules.Acesso.Contracts;
using Impa.Modules.Acesso.Application.Perfis;

namespace Impa.Modules.Acesso.Tests;

public class PerfisPadraoTests
{
    [Fact]
    public void Administrador_tem_todas_as_permissoes()
    {
        Assert.Equal(
            Permissoes.Todas.OrderBy(p => p),
            PerfisPadrao.Administrador.Permissoes.OrderBy(p => p));
    }

    [Fact]
    public void Operador_de_caixa_tem_so_as_permissoes_do_balcao()
    {
        string[] esperadas =
        [
            Permissoes.PdvVender, Permissoes.CaixaOperar, Permissoes.CaixaFechar,
            Permissoes.CatalogoVer, Permissoes.ClientesVer, Permissoes.ClientesEditar,
        ];

        Assert.Equal(esperadas.OrderBy(p => p), PerfisPadrao.OperadorDeCaixa.Permissoes.OrderBy(p => p));
        Assert.Equal("Operador de caixa", PerfisPadrao.OperadorDeCaixa.Nome);
    }

    [Fact]
    public void Lista_inicial_tem_as_15_permissoes_da_story()
    {
        Assert.Equal(15, Permissoes.Todas.Count);
        Assert.Contains("configuracao.ver", Permissoes.Todas);
        Assert.Contains("liberar.operacoes", Permissoes.Todas);
    }
}
