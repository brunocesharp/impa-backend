using Impa.Shared.Email;
using Impa.Shared.Messaging;

namespace Impa.Modules.Acesso.Application.RecuperacaoDeSenha;

/// <summary>
/// Consumidor de e-mail da recuperação de senha (Inbox do Acesso): gera o link na hora do envio (a validade de
/// 30 minutos conta a partir do e-mail) e chama o Brevo. Falha do Brevo lança: o despachante tenta de novo, com
/// intervalo crescente, e a resposta ao usuário já foi dada. Entrega pelo menos uma vez: se a confirmação se perder
/// depois do envio, sai um segundo e-mail com outro link — os dois deixam de valer quando um for usado.
/// </summary>
public sealed class EnviarEmailDeRecuperacao(IRecuperacaoDeSenha recuperacao, IEnvioDeEmail envio)
    : IIntegrationEventHandler<RecuperacaoDeSenhaSolicitada>
{
    public async Task HandleAsync(RecuperacaoDeSenhaSolicitada evento, CancellationToken ct)
    {
        // Nada a enviar (e sem falhar: o evento fica processado): desativado ou sem e-mail desde o pedido, ou pedido
        // antigo demais — num reprocessamento horas depois, ninguém espera mais esse link (D2-a da revisão 1).
        if (await recuperacao.PrepararEnvioAsync(evento.UsuarioId, evento.OcorreuEm, ct) is not { } dados)
            return;

        await envio.EnviarAsync(ModeloDoEmailDeRecuperacao.Montar(dados), ct);
    }
}
