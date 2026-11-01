using Impa.Modules.Acesso.Contracts;

namespace Impa.Modules.Acesso.Application.Perfis;

public sealed record PerfilPadrao(string Nome, IReadOnlyList<string> Permissoes);

/// <summary>Perfis criados por seed na inicialização (lista fixa até a story 1.3).</summary>
public static class PerfisPadrao
{
    public static PerfilPadrao Administrador { get; } = new("Administrador", Contracts.Permissoes.Todas);

    public static PerfilPadrao OperadorDeCaixa { get; } = new("Operador de caixa",
    [
        Contracts.Permissoes.PdvVender,
        Contracts.Permissoes.CaixaOperar,
        Contracts.Permissoes.CaixaFechar,
        Contracts.Permissoes.CatalogoVer,
        Contracts.Permissoes.ClientesVer,
        Contracts.Permissoes.ClientesEditar,
    ]);

    public static IReadOnlyList<PerfilPadrao> Todos { get; } = [Administrador, OperadorDeCaixa];
}
