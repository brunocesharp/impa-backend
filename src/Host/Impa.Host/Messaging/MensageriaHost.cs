using Impa.Shared.Messaging;

namespace Impa.Host.Messaging;

public static class MensageriaHost
{
    /// <summary>
    /// Despachante de eventos, espera por chave e reprocessamento (ADR-011 / ADR-012). Os módulos registram
    /// os próprios Outbox e handlers (<see cref="MensageriaServiceCollectionExtensions"/>).
    /// </summary>
    public static IServiceCollection AddDespachanteDeEventos(this IServiceCollection services, IConfiguration configuracao)
    {
        services.AddMensageria();
        services.AddOptions<OpcoesDaMensageria>()
            .Bind(configuracao.GetSection(OpcoesDaMensageria.Secao))
            .Validate(o => o.LimiteDeTentativas >= 1 && o.TamanhoDoLote >= 1 && o.IntervaloDeVerificacao > TimeSpan.Zero,
                "Mensageria: limite de tentativas, tamanho do lote e intervalo de verificação precisam ser positivos.")
            .ValidateOnStart();

        services.AddSingleton<ProcessadorDoOutbox>();
        services.AddSingleton<ReprocessadorDeEventos>();
        services.AddSingleton<IAguardarEventos, AguardarEventos>();
        services.AddHostedService<DespachanteDeEventos>();
        return services;
    }
}
