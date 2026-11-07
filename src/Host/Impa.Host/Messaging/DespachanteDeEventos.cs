using Impa.Shared.Messaging;
using Microsoft.Extensions.Options;

namespace Impa.Host.Messaging;

/// <summary>
/// Despachante de eventos (ADR-011): entrega os eventos dos Outbox aos handlers. Acorda logo após cada commit
/// que publicou eventos (<see cref="SinalDoDespachante"/>), a cada <see cref="OpcoesDaMensageria.IntervaloDeVerificacao"/>
/// e quando vence a próxima tentativa agendada. Erro de infraestrutura (ex.: banco fora) não derruba a API.
/// </summary>
public sealed class DespachanteDeEventos(
    ProcessadorDoOutbox processador,
    SinalDoDespachante sinal,
    IOptions<OpcoesDaMensageria> opcoes,
    TimeProvider relogio,
    ILogger<DespachanteDeEventos> log) : BackgroundService
{
    private static readonly TimeSpan EsperaMinima = TimeSpan.FromMilliseconds(20);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await processador.ProcessarAsync(stoppingToken))
                    continue; // lote cheio: lê o próximo sem esperar

                await sinal.EsperarAsync(await EsperaAsync(stoppingToken), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                log.LogError(e, "Falha no despachante de eventos; nova verificação em {Intervalo}", opcoes.Value.IntervaloDeVerificacao);
                try
                {
                    await Task.Delay(opcoes.Value.IntervaloDeVerificacao, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task<TimeSpan> EsperaAsync(CancellationToken ct)
    {
        var espera = opcoes.Value.IntervaloDeVerificacao;
        if (await processador.ProximaTentativaAsync(ct) is { } proxima)
        {
            var ate = proxima - relogio.GetUtcNow();
            if (ate < espera)
                espera = ate < EsperaMinima ? EsperaMinima : ate;
        }

        return espera;
    }
}
