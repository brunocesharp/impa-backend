namespace Impa.Modules.Acesso.Contracts;

/// <summary>Uma permissão do catálogo: código (claim <see cref="Permissoes.ClaimType"/>), grupo e texto da tela.</summary>
public sealed record PermissaoInfo(string Codigo, string Grupo, string Descricao);

/// <summary>
/// Catálogo de permissões do sistema (RN-1.4: permissões por funcionalidade). O catálogo fica no código;
/// quais permissões cada perfil tem fica no banco (claims <see cref="ClaimType"/> do perfil, story 1.3).
/// Permissão nova entra aqui e o seed a dá ao Administrador na próxima subida.
/// </summary>
public static class Permissoes
{
    public const string ClaimType = "permissao";

    public const string ConfiguracaoVer = "configuracao.ver";
    public const string ConfiguracaoFiscal = "configuracao.fiscal";
    public const string UsuariosGerenciar = "usuarios.gerenciar";
    public const string PerfisGerenciar = "perfis.gerenciar";
    public const string TerminaisGerenciar = "terminais.gerenciar";
    public const string CatalogoVer = "catalogo.ver";
    public const string CatalogoCustoVer = "catalogo.custo.ver";
    public const string CatalogoEditar = "catalogo.editar";
    public const string PdvVender = "pdv.vender";
    public const string CaixaOperar = "caixa.operar";
    public const string CaixaFechar = "caixa.fechar";
    public const string CaixaConsultar = "caixa.consultar";
    public const string ClientesVer = "clientes.ver";
    public const string ClientesEditar = "clientes.editar";
    public const string ClientesInativar = "clientes.inativar";
    public const string FornecedoresGerenciar = "fornecedores.gerenciar";
    public const string FinanceiroVer = "financeiro.ver";
    public const string FinanceiroEditar = "financeiro.editar";
    public const string ImportacaoExecutar = "importacao.executar";
    public const string LiberarOperacoes = "liberar.operacoes";

    /// <summary>RN-2.3: recebe as notificações dentro do sistema (story 4.2).</summary>
    public const string NotificacoesReceber = "notificacoes.receber";

    private const string GrupoVendas = "Vendas e caixa";
    private const string GrupoCatalogo = "Catálogo";
    private const string GrupoClientes = "Clientes";
    private const string GrupoFornecedores = "Fornecedores";
    private const string GrupoFinanceiro = "Financeiro";
    private const string GrupoAdministracao = "Administração";

    /// <summary>Catálogo na ordem da tela (agrupado).</summary>
    public static IReadOnlyList<PermissaoInfo> Catalogo { get; } =
    [
        new(PdvVender, GrupoVendas, "Vender no PDV"),
        new(CaixaOperar, GrupoVendas, "Abrir caixa, sangria e suprimento"),
        new(CaixaFechar, GrupoVendas, "Fechar caixa"),
        new(CaixaConsultar, GrupoVendas, "Consultar caixas"),
        new(LiberarOperacoes, GrupoVendas, "Liberar operações"),

        new(CatalogoVer, GrupoCatalogo, "Consultar itens"),
        new(CatalogoCustoVer, GrupoCatalogo, "Ver custo dos itens"),
        new(CatalogoEditar, GrupoCatalogo, "Cadastrar e alterar itens"),
        new(ImportacaoExecutar, GrupoCatalogo, "Importar planilhas"),

        new(ClientesVer, GrupoClientes, "Consultar clientes"),
        new(ClientesEditar, GrupoClientes, "Cadastrar e alterar clientes"),
        new(ClientesInativar, GrupoClientes, "Inativar, reativar e excluir clientes"),

        new(FornecedoresGerenciar, GrupoFornecedores, "Gerenciar fornecedores"),

        new(FinanceiroVer, GrupoFinanceiro, "Contas a pagar e financeiro"),
        new(FinanceiroEditar, GrupoFinanceiro, "Lançar, alterar, excluir, pagar e estornar contas"),

        new(ConfiguracaoVer, GrupoAdministracao, "Ver a configuração"),
        new(ConfiguracaoFiscal, GrupoAdministracao, "Alterar a configuração fiscal"),
        new(UsuariosGerenciar, GrupoAdministracao, "Gerenciar usuários"),
        new(PerfisGerenciar, GrupoAdministracao, "Gerenciar perfis e permissões"),
        new(TerminaisGerenciar, GrupoAdministracao, "Registrar e revogar terminais"),
        new(NotificacoesReceber, GrupoAdministracao, "Receber notificações"),
    ];

    public static IReadOnlyList<string> Todas { get; } = Catalogo.Select(p => p.Codigo).ToArray();

    private static readonly HashSet<string> Codigos = new(Todas, StringComparer.Ordinal);

    public static bool Existe(string codigo) => Codigos.Contains(codigo);
}
