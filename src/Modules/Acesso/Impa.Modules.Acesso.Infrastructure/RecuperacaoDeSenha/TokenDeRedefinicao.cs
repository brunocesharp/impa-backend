using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace Impa.Modules.Acesso.Infrastructure.RecuperacaoDeSenha;

/// <summary>
/// Token do link de redefinição (RN-1.11): id do usuário, security stamp do Identity e vencimento, cifrados e
/// autenticados pelo DataProtection (as mesmas chaves do cookie de sessão). Uso único: redefinir troca o stamp, e
/// todo token emitido antes deixa de conferir. O vencimento é do <see cref="TimeProvider"/> (testável), e não do
/// relógio do sistema como no <c>DataProtectorTokenProvider</c>; o id viaja no token porque o contrato
/// <c>POST /api/auth/redefinir-senha</c> recebe só <c>{ token, novaSenha }</c>.
/// </summary>
public sealed class TokenDeRedefinicao(IDataProtectionProvider dataProtection, TimeProvider relogio)
{
    private const string Proposito = "Impa.Acesso.RecuperacaoDeSenha.v1";

    private readonly IDataProtector _protetor = dataProtection.CreateProtector(Proposito);

    public string Gerar(Guid usuarioId, string securityStamp, TimeSpan validade)
    {
        var expira = relogio.GetUtcNow().Add(validade).ToUnixTimeSeconds();
        var conteudo = string.Join('|', usuarioId.ToString("N"), securityStamp,
            expira.ToString(CultureInfo.InvariantCulture));
        return Base64Url.EncodeToString(_protetor.Protect(Encoding.UTF8.GetBytes(conteudo)));
    }

    /// <summary>O usuário e o stamp do token, se ele for autêntico e não tiver vencido; senão <c>null</c>.</summary>
    public (Guid UsuarioId, string SecurityStamp)? Ler(string token)
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
        if (partes.Length != 3
            || !Guid.TryParseExact(partes[0], "N", out var usuarioId)
            || !long.TryParse(partes[2], NumberStyles.None, CultureInfo.InvariantCulture, out var expira))
            return null;

        return relogio.GetUtcNow() <= DateTimeOffset.FromUnixTimeSeconds(expira) ? (usuarioId, partes[1]) : null;
    }
}
