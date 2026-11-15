using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace Impa.Modules.Acesso.Infrastructure.RecuperacaoDeSenha;

/// <summary>Conteúdo de um token autêntico e dentro do prazo.</summary>
public sealed record TokenLido(Guid UsuarioId, string SecurityStamp, string HashDoEmail)
{
    /// <summary>
    /// O link foi enviado para este e-mail (normalizado)? R10 da revisão 1: e-mail trocado ou apagado pelo
    /// administrador depois do envio invalida o link, porque a caixa antiga pode não ser mais do usuário.
    /// </summary>
    public bool FoiEnviadoPara(string? emailNormalizado) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(HashDoEmail),
            Encoding.ASCII.GetBytes(TokenDeRedefinicao.Hash(emailNormalizado)));

    public override string ToString() => $"{nameof(TokenLido)} {{ UsuarioId = {UsuarioId}, SecurityStamp = *** }}";
}

/// <summary>
/// Token do link de redefinição (RN-1.11): id do usuário, security stamp do Identity, hash do e-mail de destino e
/// vencimento, cifrados e autenticados pelo DataProtection (as mesmas chaves do cookie de sessão). Uso único:
/// redefinir troca o stamp, e todo token emitido antes deixa de conferir. O vencimento é do <see cref="TimeProvider"/>
/// (testável), e não do relógio do sistema como no <c>DataProtectorTokenProvider</c>; o id viaja no token porque o
/// contrato <c>POST /api/auth/redefinir-senha</c> recebe só <c>{ token, novaSenha }</c>.
/// </summary>
public sealed class TokenDeRedefinicao(IDataProtectionProvider dataProtection, TimeProvider relogio)
{
    // v2: acrescentou o hash do e-mail (R10). Um token v1 não abre com o protetor v2 e é recusado como inválido.
    private const string Proposito = "Impa.Acesso.RecuperacaoDeSenha.v2";

    private readonly IDataProtector _protetor = dataProtection.CreateProtector(Proposito);

    /// <param name="emailNormalizado">E-mail de destino, já normalizado (<c>UserManager.NormalizeEmail</c>).</param>
    public string Gerar(Guid usuarioId, string securityStamp, string emailNormalizado, TimeSpan validade)
    {
        var expira = relogio.GetUtcNow().Add(validade).ToUnixTimeSeconds();
        var conteudo = string.Join('|', usuarioId.ToString("N"), securityStamp, Hash(emailNormalizado),
            expira.ToString(CultureInfo.InvariantCulture));
        return Base64Url.EncodeToString(_protetor.Protect(Encoding.UTF8.GetBytes(conteudo)));
    }

    /// <summary>O conteúdo do token, se ele for autêntico e não tiver vencido; senão <c>null</c>.</summary>
    public TokenLido? Ler(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        string conteudo;
        try
        {
            conteudo = Encoding.UTF8.GetString(_protetor.Unprotect(Base64Url.DecodeFromChars(token.Trim())));
        }
        catch (Exception e) when (e is FormatException or CryptographicException)
        {
            return null; // forjado, truncado ou de outra chave
        }

        var partes = conteudo.Split('|');
        if (partes.Length != 4
            || !Guid.TryParseExact(partes[0], "N", out var usuarioId)
            || !long.TryParse(partes[3], NumberStyles.None, CultureInfo.InvariantCulture, out var expira))
            return null;

        return relogio.GetUtcNow() <= DateTimeOffset.FromUnixTimeSeconds(expira)
            ? new TokenLido(usuarioId, partes[1], partes[2])
            : null;
    }

    /// <summary>SHA-256 do e-mail normalizado (sem e-mail: o hash do vazio, que nenhum e-mail real confere).</summary>
    internal static string Hash(string? emailNormalizado) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(emailNormalizado ?? string.Empty)));
}
