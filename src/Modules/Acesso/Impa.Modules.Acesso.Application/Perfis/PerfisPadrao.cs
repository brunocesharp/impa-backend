using Impa.Modules.Acesso.Contracts;

namespace Impa.Modules.Acesso.Application.Perfis;

/// <param name="Protegido">RN-1.6: não pode ser excluído nem alterado, e sempre tem todas as permissões.</param>
public sealed record PerfilPadrao(string Nome, IReadOnlyList<string> Permissoes, bool Protegido);

/// <summary>
/// Perfis criados pelo seed na instalação (RN-1.5). Depois de criados, o Operador de caixa é do administrador
/// (pode ser alterado pela tela); o Administrador é protegido e o seed mantém nele todas as permissões do catálogo.
/// </summary>
public static class PerfisPadrao
{
    public static PerfilPadrao Administrador { get; } = new("Administrador", Contracts.Permissoes.Todas, Protegido: true);

    public static PerfilPadrao OperadorDeCaixa { get; } = new("Operador de caixa",
    [
        Contracts.Permissoes.PdvVender,
        Contracts.Permissoes.CaixaOperar,
        Contracts.Permissoes.CaixaFechar,
        Contracts.Permissoes.CatalogoVer,
        Contracts.Permissoes.ClientesVer,
        Contracts.Permissoes.ClientesEditar,
    ], Protegido: false);

    public static IReadOnlyList<PerfilPadrao> Todos { get; } = [Administrador, OperadorDeCaixa];
}
