namespace Impa.Modules.Acesso.Contracts;

/// <summary>
/// Permissões do sistema, guardadas como claims <see cref="ClaimType"/> nos perfis.
/// Lista fixa nesta fase; a gestão de perfis pela tela vem na story 1.3.
/// </summary>
public static class Permissoes
{
    public const string ClaimType = "permissao";

    public const string ConfiguracaoVer = "configuracao.ver";
    public const string ConfiguracaoFiscal = "configuracao.fiscal";
    public const string UsuariosGerenciar = "usuarios.gerenciar";
    public const string CatalogoVer = "catalogo.ver";
    public const string CatalogoCustoVer = "catalogo.custo.ver";
    public const string CatalogoEditar = "catalogo.editar";
    public const string PdvVender = "pdv.vender";
    public const string CaixaOperar = "caixa.operar";
    public const string CaixaFechar = "caixa.fechar";
    public const string CaixaConsultar = "caixa.consultar";
    public const string ClientesVer = "clientes.ver";
    public const string ClientesEditar = "clientes.editar";
    public const string FinanceiroVer = "financeiro.ver";
    public const string ImportacaoExecutar = "importacao.executar";
    public const string LiberarOperacoes = "liberar.operacoes";

    public static IReadOnlyList<string> Todas { get; } =
    [
        ConfiguracaoVer, ConfiguracaoFiscal, UsuariosGerenciar,
        CatalogoVer, CatalogoCustoVer, CatalogoEditar,
        PdvVender, CaixaOperar, CaixaFechar, CaixaConsultar,
        ClientesVer, ClientesEditar, FinanceiroVer, ImportacaoExecutar, LiberarOperacoes,
    ];
}
