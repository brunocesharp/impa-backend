using System.Buffers.Text;
using System.Security.Cryptography;

namespace Impa.Modules.Acesso.Application.Terminais;

/// <summary>
/// Token do terminal (story 1-6, T1): 256 bits aleatórios, em Base64Url no cookie do dispositivo. O banco guarda só o
/// SHA-256 do token — com 256 bits de entropia não há o que adivinhar, então um hash rápido basta (não é senha).
/// </summary>
public static class TokenDoTerminal
{
    public const int Bytes = 32;

    /// <summary>Tamanho do token em Base64Url sem padding (32 bytes → 43 caracteres).</summary>
    public static readonly int Caracteres = Base64Url.GetEncodedLength(Bytes);

    public static string Gerar() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(Bytes));

    public static byte[] Hash(string token) => SHA256.HashData(Base64Url.DecodeFromChars(token));

    /// <summary>Hash do token, ou <c>null</c> quando o valor (ex.: cookie adulterado) não é um token válido.</summary>
    public static byte[]? HashSeValido(string? token)
    {
        if (token is null || token.Length != Caracteres || !Base64Url.IsValid(token, out var tamanho) || tamanho != Bytes)
            return null;

        return Hash(token);
    }
}
