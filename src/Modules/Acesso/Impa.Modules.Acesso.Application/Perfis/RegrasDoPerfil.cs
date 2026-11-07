using FluentValidation;
using Impa.Modules.Acesso.Contracts;

namespace Impa.Modules.Acesso.Application.Perfis;

/// <summary>Validações comuns de nome e permissões (criar e alterar perfil).</summary>
internal static class RegrasDoPerfil
{
    public const int NomeMaximo = 60;

    public static void Nome<T>(IRuleBuilder<T, string> regra) => regra
        .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Informe o nome do perfil")
        .Must(n => n is null || n.Trim().Length <= NomeMaximo).WithMessage($"O nome do perfil tem no máximo {NomeMaximo} caracteres");

    public static void Permissoes<T>(IRuleBuilder<T, IReadOnlyList<string>> regra) => regra
        .NotNull().WithMessage("Informe as permissões")
        .Must(ps => ps is null || ps.All(Contracts.Permissoes.Existe))
        .WithMessage("Permissão desconhecida");

    public static string NormalizarNome(string nome) => nome.Trim();

    public static IReadOnlyCollection<string> NormalizarPermissoes(IEnumerable<string> permissoes) =>
        permissoes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
}
