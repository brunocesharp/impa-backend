namespace Impa.Shared.Email;

/// <summary>Um e-mail a enviar: um destinatário, assunto e corpo em HTML e em texto puro.</summary>
public sealed record MensagemDeEmail(string Para, string? NomeDoDestinatario, string Assunto, string Html, string Texto)
{
    // O corpo pode levar links de uso único (recuperação de senha): fora dos logs.
    public override string ToString() => $"{nameof(MensagemDeEmail)} {{ Assunto = {Assunto} }}";
}

/// <summary>
/// Componente "Envio de e-mail" da API (ADR-009): toda mensagem do IMPA sai por aqui, hoje pela API transacional
/// do Brevo. Chamado de um handler de evento (Outbox, ADR-011), nunca no fluxo principal: quem chama não espera o
/// provedor para responder ao usuário, e uma <see cref="FalhaNoEnvioDeEmail"/> faz o despachante tentar de novo.
/// </summary>
public interface IEnvioDeEmail
{
    /// <exception cref="FalhaNoEnvioDeEmail">O provedor recusou ou não respondeu.</exception>
    Task EnviarAsync(MensagemDeEmail mensagem, CancellationToken ct);
}

/// <summary>O provedor de e-mail recusou a mensagem ou ficou fora do ar. A mensagem não traz a chave da API.</summary>
public sealed class FalhaNoEnvioDeEmail(string mensagem, Exception? causa = null) : Exception(mensagem, causa);
