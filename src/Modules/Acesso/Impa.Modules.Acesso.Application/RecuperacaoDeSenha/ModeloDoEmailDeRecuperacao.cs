using System.Globalization;
using System.Net;
using Impa.Shared.Email;

namespace Impa.Modules.Acesso.Application.RecuperacaoDeSenha;

/// <summary>
/// E-mail de redefinição de senha (T6): logotipo, texto curto, botão e o aviso de validade. HTML em tabelas e
/// estilos inline (clientes de e-mail ignoram CSS externo); a ação principal é preta, como no tema claro da
/// direção de arte. A versão em texto puro leva o mesmo link, para quem não abre HTML.
/// </summary>
public static class ModeloDoEmailDeRecuperacao
{
    public const string Assunto = "Redefinição de senha";

    public static MensagemDeEmail Montar(EnvioDoLink dados)
    {
        ArgumentNullException.ThrowIfNull(dados);
        var minutos = ((int)dados.Validade.TotalMinutes).ToString(CultureInfo.InvariantCulture);
        var nome = WebUtility.HtmlEncode(dados.Nome);
        var login = WebUtility.HtmlEncode(dados.Login);
        var empresa = WebUtility.HtmlEncode(dados.NomeDaEmpresa);
        var link = WebUtility.HtmlEncode(dados.Link);
        var logo = WebUtility.HtmlEncode(dados.UrlDoLogo);

        var html = $$"""
            <!doctype html>
            <html lang="pt-BR">
            <body style="margin:0;padding:0;background:#f4f4f5;font-family:Roboto,Arial,Helvetica,sans-serif;color:#18181b;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f4f4f5;padding:24px 16px;">
                <tr><td align="center">
                  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:480px;background:#ffffff;border-radius:12px;padding:32px 28px;">
                    <tr><td style="padding-bottom:24px;"><img src="{{logo}}" alt="{{empresa}}" height="40" style="display:block;height:40px;border:0;"></td></tr>
                    <tr><td style="font-size:20px;font-weight:600;padding-bottom:12px;">Redefinir sua senha</td></tr>
                    <tr><td style="font-size:15px;line-height:22px;padding-bottom:24px;">
                      Olá, {{nome}}. Recebemos um pedido para redefinir a senha do usuário <strong>{{login}}</strong> no IMPA.
                      Use o botão abaixo para escolher uma nova senha.
                    </td></tr>
                    <tr><td style="padding-bottom:24px;">
                      <a href="{{link}}" style="display:inline-block;background:#111111;color:#ffffff;text-decoration:none;font-size:15px;font-weight:600;padding:12px 24px;border-radius:24px;">Definir nova senha</a>
                    </td></tr>
                    <tr><td style="font-size:13px;line-height:20px;color:#52525b;">
                      O link vale por <strong>{{minutos}} minutos</strong> e só pode ser usado uma vez. Ao definir a nova senha,
                      as sessões abertas com a senha antiga são encerradas.<br><br>
                      Se você não pediu a redefinição, ignore este e-mail: sua senha continua a mesma.
                    </td></tr>
                  </table>
                  <p style="font-size:12px;color:#71717a;margin:16px 0 0;">{{empresa}} · e-mail automático, não responda</p>
                </td></tr>
              </table>
            </body>
            </html>
            """;

        var texto = $"""
            Olá, {dados.Nome}.

            Recebemos um pedido para redefinir a senha do usuário {dados.Login} no IMPA.
            Para escolher uma nova senha, abra o link abaixo:

            {dados.Link}

            O link vale por {minutos} minutos e só pode ser usado uma vez. Ao definir a nova senha, as sessões abertas com a senha antiga são encerradas.

            Se você não pediu a redefinição, ignore este e-mail: sua senha continua a mesma.

            {dados.NomeDaEmpresa} · e-mail automático, não responda
            """;

        return new MensagemDeEmail(dados.Email, dados.Nome, Assunto, html, texto);
    }
}
