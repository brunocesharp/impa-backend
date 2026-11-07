using Impa.Shared.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Impa.Host.Messaging;

/// <summary>Volta um evento "com falha" para pendente, com as tentativas zeradas, e acorda o despachante.</summary>
public sealed class ReprocessadorDeEventos(
    IServiceScopeFactory escopos,
    RegistroDeMensageria registro,
    SinalDoDespachante sinal,
    TimeProvider relogio,
    ILogger<ReprocessadorDeEventos> log)
{
    /// <returns><c>false</c> se nenhum Outbox tem esse evento com falha.</returns>
    public async Task<bool> ReprocessarAsync(Guid eventoId, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        foreach (var tipoDoOutbox in registro.Outboxes)
        {
            await using var escopo = escopos.CreateAsyncScope();
            var contexto = (DbContext)escopo.ServiceProvider.GetRequiredService(tipoDoOutbox);
            var alterados = await contexto.Set<OutboxMensagem>()
                .Where(m => m.Id == eventoId && m.Situacao == SituacaoDoEvento.ComFalha)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.Situacao, SituacaoDoEvento.Pendente)
                    .SetProperty(m => m.Tentativas, 0)
                    .SetProperty(m => m.ProximaTentativaEm, agora), ct);

            if (alterados > 0)
            {
                log.LogInformation("Evento {EventoId} reprocessado ({Outbox})", eventoId, tipoDoOutbox.Name);
                sinal.Acordar();
                return true;
            }
        }

        return false;
    }
}
