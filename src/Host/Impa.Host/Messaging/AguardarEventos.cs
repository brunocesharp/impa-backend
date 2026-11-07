using System.Diagnostics;
using Impa.Shared.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Impa.Host.Messaging;

/// <summary>
/// Espera por chave (ADR-012, regra 5). Pendente para o consumidor = evento com a chave, ainda não entregue
/// a todos, de um tipo que o consumidor trata, sem a linha de algum handler dele no Inbox. Olhar o Inbox
/// (e não só a situação do Outbox) libera a espera assim que o efeito do consumidor foi gravado.
/// </summary>
public sealed class AguardarEventos(IServiceScopeFactory escopos, RegistroDeMensageria registro, SinalDoDespachante sinal)
    : IAguardarEventos
{
    private static readonly TimeSpan IntervaloDeConsulta = TimeSpan.FromMilliseconds(50);

    public async Task<bool> AguardarAsync(string chave, string consumidor, TimeSpan timeout, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chave);
        var handlers = registro.HandlersDoConsumidor(consumidor).ToList();
        if (handlers.Count == 0) // nome errado não pode virar "nada pendente" (revisão 1, A3)
            throw new ArgumentException(
                $"Consumidor '{consumidor}' sem handler registrado. Registrados: " +
                string.Join(", ", registro.Handlers.Select(h => h.Consumidor).Distinct()),
                nameof(consumidor));

        var cronometro = Stopwatch.StartNew();
        var acordou = false;
        while (await HaPendentesAsync(chave, handlers, ct))
        {
            var restante = timeout - cronometro.Elapsed;
            if (restante <= TimeSpan.Zero)
                return false;

            if (!acordou)
            {
                sinal.Acordar(); // quem espera tem pressa: não aguarda a verificação periódica
                acordou = true;
            }

            await Task.Delay(restante < IntervaloDeConsulta ? restante : IntervaloDeConsulta, ct);
        }

        return true;
    }

    private async Task<bool> HaPendentesAsync(string chave, List<RegistroDeHandler> handlers, CancellationToken ct)
    {
        var tipos = handlers.Select(h => h.Tipo).Distinct().ToList();
        await using var escopo = escopos.CreateAsyncScope();

        foreach (var tipoDoOutbox in registro.Outboxes)
        {
            var outbox = (DbContext)escopo.ServiceProvider.GetRequiredService(tipoDoOutbox);
            var pendentes = await outbox.Set<OutboxMensagem>()
                .AsNoTracking()
                .Where(m => m.ChaveDeCorrelacao == chave && m.Situacao != SituacaoDoEvento.Entregue && tipos.Contains(m.Tipo))
                .Select(m => new { m.Id, m.Tipo, m.Versao })
                .ToListAsync(ct);

            foreach (var evento in pendentes)
            {
                var porContexto = handlers
                    .Where(h => h.Tipo == evento.Tipo && h.Versao == evento.Versao)
                    .GroupBy(h => h.ContextoDoConsumidor);
                foreach (var grupo in porContexto)
                {
                    var nomes = grupo.Select(h => h.Nome).ToList();
                    var consumidor = (DbContext)escopo.ServiceProvider.GetRequiredService(grupo.Key);
                    var processados = await consumidor.Set<InboxMensagem>()
                        .CountAsync(i => i.EventoId == evento.Id && nomes.Contains(i.Handler), ct);
                    if (processados < nomes.Count)
                        return true;
                }
            }
        }

        return false;
    }
}
