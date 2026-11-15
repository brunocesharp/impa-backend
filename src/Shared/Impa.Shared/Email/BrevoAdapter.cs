using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Impa.Shared.Email;

/// <summary>
/// Envio pela API transacional do Brevo: <c>POST /v3/smtp/email</c> com o cabeçalho <c>api-key</c> (ADR-009).
/// Sem novas tentativas aqui: quem tenta de novo é o despachante do Outbox (ADR-011), com intervalo crescente;
/// repetir também aqui multiplicaria o mesmo e-mail quando o Brevo aceita mas a resposta se perde.
/// </summary>
public sealed class BrevoAdapter(HttpClient http, IOptions<OpcoesDeEmail> opcoes, ILogger<BrevoAdapter> log) : IEnvioDeEmail
{
    private const string Rota = "smtp/email";

    public async Task EnviarAsync(MensagemDeEmail mensagem, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(mensagem);
        var o = opcoes.Value;
        var corpo = new EnviarEmailApiRequest(
            new ContatoApi(o.Remetente, o.NomeDoRemetente),
            [new ContatoApi(mensagem.Para, mensagem.NomeDoDestinatario)],
            mensagem.Assunto, mensagem.Html, mensagem.Texto);

        using var requisicao = new HttpRequestMessage(HttpMethod.Post, Rota) { Content = JsonContent.Create(corpo) };
        requisicao.Headers.Add("api-key", o.Brevo.ChaveDaApi);
        requisicao.Headers.Accept.ParseAdd("application/json");

        HttpResponseMessage resposta;
        try
        {
            // Só os cabeçalhos: com o corpo lido aqui dentro, uma falha na leitura depois de um 2xx viraria
            // HttpRequestException e o despachante reenviaria um e-mail que o Brevo já aceitou (R8, revisão 1).
            resposta = await http.SendAsync(requisicao, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new FalhaNoEnvioDeEmail("Brevo fora do alcance ou sem resposta no prazo.", e);
        }

        using (resposta)
        {
            if (resposta.IsSuccessStatusCode)
            {
                var messageId = await LerMessageIdAsync(resposta, ct);
                log.LogInformation("E-mail \"{Assunto}\" aceito pelo Brevo ({MessageId})", mensagem.Assunto, messageId);
                return;
            }

            // O corpo de erro do Brevo é { code, message }: vai para o log, sem o destinatário nem o conteúdo.
            string detalhe;
            try
            {
                detalhe = await resposta.Content.ReadAsStringAsync(ct);
            }
            catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                detalhe = "(corpo ilegível)";
            }

            throw new FalhaNoEnvioDeEmail(
                $"Brevo recusou o e-mail \"{mensagem.Assunto}\": HTTP {(int)resposta.StatusCode} {Recortar(detalhe)}");
        }
    }

    /// <summary>
    /// Depois de um 2xx o e-mail já foi aceito: nada aqui pode lançar (nem JSON inválido, nem a conexão caindo no meio
    /// do corpo, nem o cancelamento), senão o despachante reenviaria. Só o id da mensagem fica sem registro.
    /// </summary>
    private async Task<string?> LerMessageIdAsync(HttpResponseMessage resposta, CancellationToken ct)
    {
        try
        {
            return (await resposta.Content.ReadFromJsonAsync<EnviarEmailApiResponse>(ct))?.MessageId;
        }
        catch (Exception e)
        {
            log.LogWarning("Brevo aceitou o e-mail, mas a resposta não pôde ser lida ({Erro})", e.GetType().Name);
            return null;
        }
    }

    private static string Recortar(string texto) => texto.Length <= 300 ? texto : texto[..300];

    private sealed record ContatoApi(
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Nome);

    private sealed record EnviarEmailApiRequest(
        [property: JsonPropertyName("sender")] ContatoApi Remetente,
        [property: JsonPropertyName("to")] IReadOnlyList<ContatoApi> Para,
        [property: JsonPropertyName("subject")] string Assunto,
        [property: JsonPropertyName("htmlContent")] string Html,
        [property: JsonPropertyName("textContent")] string Texto);

    private sealed record EnviarEmailApiResponse([property: JsonPropertyName("messageId")] string? MessageId);
}

/// <summary>
/// Envio desligado: sem <c>Email:Brevo:ChaveDaApi</c>, ou com a chave e sem o remetente (<paramref name="motivo"/>).
/// Em desenvolvimento e testes (<paramref name="descartar"/>) só registra no log que o e-mail não saiu, sem o
/// conteúdo, que pode ter um link de uso único. Senão registra o erro e lança <see cref="FalhaNoEnvioDeEmail"/>: a API
/// continua no ar (ADR-009) e o evento fica "com falha" no Outbox, reprocessável depois de corrigir a configuração.
/// </summary>
public sealed class EnvioDeEmailDesligado(
    ILogger<EnvioDeEmailDesligado> log,
    bool descartar = true,
    string motivo = "configure Email__Brevo__ChaveDaApi") : IEnvioDeEmail
{
    public Task EnviarAsync(MensagemDeEmail mensagem, CancellationToken ct)
    {
        if (!descartar)
        {
            log.LogError("Envio de e-mail desligado ({Motivo}): \"{Assunto}\" não foi enviado", motivo, mensagem.Assunto);
            throw new FalhaNoEnvioDeEmail($"Envio de e-mail desligado: {motivo}. \"{mensagem.Assunto}\" não foi enviado.");
        }

        log.LogWarning("Envio de e-mail desligado ({Motivo}): \"{Assunto}\" não foi enviado", motivo, mensagem.Assunto);
        return Task.CompletedTask;
    }
}
