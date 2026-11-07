using Impa.Modules.Acesso.Infrastructure.Sessoes;
using Microsoft.Extensions.Options;

namespace Impa.Host.Tarefas;

/// <summary>
/// Limpeza das sessões antigas em segundo plano (story 1-11): uma vez logo que a API sobe e depois a cada
/// <see cref="LimpezaDeSessoesOptions.Intervalo"/>, no relógio injetado. Cada execução usa um escopo novo
/// (o banco do módulo é scoped) e loga quantas sessões apagou; uma falha é logada e não impede a próxima.
/// </summary>
public sealed class LimpezaDeSessoesTarefa(
    IServiceScopeFactory escopos,
    IOptions<LimpezaDeSessoesOptions> opcoes,
    TimeProvider relogio,
    ILogger<LimpezaDeSessoesTarefa> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Criado antes da primeira execução: o intervalo conta a partir da subida da API.
        using var timer = new PeriodicTimer(opcoes.Value.Intervalo, relogio);
        try
        {
            do
            {
                await ExecutarAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // desligamento da API
        }
    }

    private async Task ExecutarAsync(CancellationToken stoppingToken)
    {
        var retencao = opcoes.Value.Retencao;
        try
        {
            await using var escopo = escopos.CreateAsyncScope();
            var apagadas = await escopo.ServiceProvider.GetRequiredService<ILimpezaDeSessoes>()
                .ApagarAntigasAsync(retencao, stoppingToken);
            log.LogInformation("Limpeza de sessões: {Apagadas} sessões apagadas (terminadas há mais de {RetencaoEmDias} dias)",
                apagadas, opcoes.Value.RetencaoEmDias);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            log.LogError(e, "Falha na limpeza de sessões; nova execução em {Intervalo}", opcoes.Value.Intervalo);
        }
    }
}
