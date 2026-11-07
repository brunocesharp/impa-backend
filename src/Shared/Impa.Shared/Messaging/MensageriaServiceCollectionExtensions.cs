using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Impa.Shared.Messaging;

/// <summary>
/// Registro da mensageria por módulo (ADR-011). No <c>DependencyInjection</c> da Infrastructure do módulo:
/// <code>
/// services.AddDbContext&lt;VendasDbContext&gt;((sp, o) => o.UseNpgsql(cs, ...).UsarOutbox(sp));
/// services.AddOutbox&lt;VendasDbContext&gt;();                                  // módulo que publica
/// services.AddIntegrationEventHandler&lt;VendaFinalizada, BaixarEstoque, EstoqueDbContext&gt;("Estoque"); // que consome
/// </code>
/// O despachante, a espera por chave e o reprocessamento são registrados pelo Host.
/// </summary>
public static class MensageriaServiceCollectionExtensions
{
    /// <summary>Serviços comuns (idempotente): registro, serializador, sinal, interceptor e publicador.</summary>
    public static RegistroDeMensageria AddMensageria(this IServiceCollection services)
    {
        var existente = services.FirstOrDefault(d => d.ServiceType == typeof(RegistroDeMensageria))?.ImplementationInstance;
        if (existente is RegistroDeMensageria registro)
            return registro;

        registro = new RegistroDeMensageria();
        services.AddSingleton(registro);
        services.AddSingleton(registro.Serializador);
        services.TryAddSingleton<SinalDoDespachante>();
        services.TryAddSingleton<InterceptorDoOutbox>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAdd(ServiceDescriptor.Scoped(typeof(IEventPublisher<>), typeof(EventPublisher<>)));
        return registro;
    }

    /// <summary>Inclui o Outbox do contexto na leitura do despachante e libera o <see cref="IEventPublisher{TContexto}"/>.</summary>
    public static IServiceCollection AddOutbox<TContexto>(this IServiceCollection services) where TContexto : DbContext
    {
        services.AddMensageria().AdicionarOutbox(typeof(TContexto));
        return services;
    }

    /// <summary>
    /// Registra o handler de um evento no módulo <paramref name="consumidor"/>. O despachante roda o handler
    /// numa transação do <typeparamref name="TContexto"/>, que precisa mapear o Inbox (<c>MapearInbox</c>).
    /// </summary>
    public static IServiceCollection AddIntegrationEventHandler<TEvento, THandler, TContexto>(
        this IServiceCollection services, string consumidor)
        where TEvento : IIntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvento>
        where TContexto : DbContext
    {
        services.AddMensageria().AdicionarHandler<TEvento, THandler>(typeof(TContexto), consumidor);
        services.TryAddScoped<THandler>();
        return services;
    }

    /// <summary>Liga o interceptor que acorda o despachante após o commit que publicou eventos.</summary>
    public static DbContextOptionsBuilder UsarOutbox(this DbContextOptionsBuilder options, IServiceProvider servicos) =>
        options.AddInterceptors(servicos.GetRequiredService<InterceptorDoOutbox>());
}
