using System.Security.Cryptography;
using FluentValidation;

namespace Impa.Modules.Acesso.Application.Usuarios;

/// <summary>Validações comuns de criar e alterar usuário, e a senha provisória (RN-1.8).</summary>
public static class RegrasDoUsuario
{
    public const int LoginMaximo = 50;
    public const int NomeMaximo = 120;
    public const int EmailMaximo = 256;

    /// <summary>RN-1.1: mínimo de 8 caracteres, sem exigir dígito, símbolo ou maiúscula.</summary>
    public const int SenhaMinima = 8;

    public const string MensagemSenhaCurta = "A senha precisa ter pelo menos 8 caracteres";

    /// <summary>Caracteres aceitos no login (os do <c>AllowedUserNameCharacters</c> padrão do Identity).</summary>
    private const string CaracteresDoLogin = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+";

    /// <summary>Senha provisória gerada pelo sistema: <c>temp-</c> + 4 dígitos (story 1.4, T2).</summary>
    public static string GerarSenhaProvisoria() => $"temp-{RandomNumberGenerator.GetInt32(0, 10_000):D4}";

    public static void Login<T>(IRuleBuilder<T, string> regra) => regra
        .Must(l => !string.IsNullOrWhiteSpace(l)).WithMessage("Informe o usuário")
        .Must(l => l is null || l.Trim().Length <= LoginMaximo).WithMessage($"O usuário tem no máximo {LoginMaximo} caracteres")
        .Must(l => l is null || l.Trim().All(CaracteresDoLogin.Contains))
        .WithMessage("O usuário aceita letras sem acento, números e . _ - @ +");

    public static void Nome<T>(IRuleBuilder<T, string> regra) => regra
        .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Informe o nome")
        .Must(n => n is null || n.Trim().Length <= NomeMaximo).WithMessage($"O nome tem no máximo {NomeMaximo} caracteres");

    public static void Email<T>(IRuleBuilder<T, string?> regra) => regra
        .Must(e => string.IsNullOrWhiteSpace(e) || e.Trim().Length <= EmailMaximo)
        .WithMessage($"O e-mail tem no máximo {EmailMaximo} caracteres")
        .Must(e => string.IsNullOrWhiteSpace(e) || EmailValido(e.Trim())).WithMessage("E-mail inválido");

    /// <summary>Senha informada pelo administrador (opcional: sem ela, o sistema gera).</summary>
    public static void SenhaOpcional<T>(IRuleBuilder<T, string?> regra) => regra
        .Must(s => s is null || s.Length >= SenhaMinima).WithMessage(MensagemSenhaCurta);

    public static string NormalizarTexto(string texto) => texto.Trim();

    public static string? NormalizarEmail(string? email) => string.IsNullOrWhiteSpace(email) ? null : email.Trim();

    private static bool EmailValido(string email)
    {
        var arroba = email.IndexOf('@');
        return arroba > 0 && arroba == email.LastIndexOf('@') && arroba < email.Length - 1
               && !email.Any(char.IsWhiteSpace) && email[(arroba + 1)..].Contains('.');
    }
}
