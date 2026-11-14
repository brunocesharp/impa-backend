using System.Buffers.Text;
using System.Security.Cryptography;

namespace Impa.Modules.Acesso.Application.Liberacoes;

/// <summary>
/// Token da liberação (story 1-5, T2): 256 bits aleatórios em Base64Url, enviado pela SPA no cabeçalho
/// <c>X-Liberacao</c>. O banco guarda só o SHA-256 — com 256 bits de entropia, um hash rápido basta (não é senha).
/// </summary>
public static class TokenDeLiberacao
{
    public const int Bytes = 32;

    /// <summary>RN-1.9: a liberação vale só para aquela operação — e por pouco tempo (2 minutos).</summary>
    public static readonly TimeSpan Validade = TimeSpan.FromMinutes(2);

    public static readonly int Caracteres = Base64Url.GetEncodedLength(Bytes);

    public static string Gerar() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(Bytes));

    public static byte[] Hash(string token) => SHA256.HashData(Base64Url.DecodeFromChars(token));

    /// <summary>Hash do token, ou <c>null</c> quando o valor do cabeçalho não é um token válido.</summary>
    public static byte[]? HashSeValido(string? token)
    {
        if (token is null || token.Length != Caracteres || !Base64Url.IsValid(token, out var tamanho) || tamanho != Bytes)
            return null;

        return Hash(token);
    }
}
