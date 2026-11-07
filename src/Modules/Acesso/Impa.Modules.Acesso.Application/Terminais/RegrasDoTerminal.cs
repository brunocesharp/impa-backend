using FluentValidation;

namespace Impa.Modules.Acesso.Application.Terminais;

/// <summary>Validação do nome do terminal (registrar e renomear).</summary>
public static class RegrasDoTerminal
{
    public const int NomeMaximo = 60;

    public static void Nome<T>(IRuleBuilder<T, string> regra) => regra
        .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Informe o nome do terminal")
        .Must(n => n is null || n.Trim().Length <= NomeMaximo).WithMessage($"O nome do terminal tem no máximo {NomeMaximo} caracteres");

    public static string NormalizarNome(string nome) => nome.Trim();
}
