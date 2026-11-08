using Impa.Modules.Configuracao.Application.Auditoria;
using Impa.Modules.Configuracao.Application.Notificacoes;
using Impa.Modules.Configuracao.Contracts.Auditoria;
using Impa.Modules.Configuracao.Infrastructure.Auditoria;
using Impa.Modules.Configuracao.Infrastructure.Notificacoes;
using Impa.Modules.Configuracao.Infrastructure.Persistence;
using Impa.Shared.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Impa.Modules.Configuracao.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Nome do consumidor no registro da mensageria (Inbox e espera por chave).</summary>
    public const string Consumidor = "Configuracao";

    /// <summary>
    /// Registra o módulo Configuração e Notificações: banco (schema <c>configuracao</c>, com Outbox e Inbox),
    /// a central de notificações, a base dos handlers de fato e a auditoria das operações sensíveis. Depende de <c>IUsuariosComPermissao</c> (Acesso).
    /// </summary>
    public static IServiceCollection AddConfiguracaoModule(this IServiceCollection services, string connectionString)
    {
        services.AddMensageria();
        services.AddDbContext<ConfiguracaoDbContext>((sp, options) => options
            .UseNpgsql(connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", ConfiguracaoDbContext.Schema))
            .UsarOutbox(sp));
        services.AddOutbox<ConfiguracaoDbContext>(); // publica NotificacaoCriada (story 4.3 consome)

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<INotificacoes, RepositorioDeNotificacoes>();
        services.AddScoped<IEventosDaConfiguracao, EventosDaConfiguracao>();
        services.AddScoped<CriacaoDeNotificacoes>();

        // Auditoria (story 4.4): cada módulo publica OperacaoSensivelAuditada no próprio Outbox; grava aqui, uma vez (Inbox).
        services.AddScoped<IRegistrosDeAuditoria, RepositorioDeAuditoria>();
        services.AddIntegrationEventHandler<OperacaoSensivelAuditada, GravarOperacaoSensivel, ConfiguracaoDbContext>(Consumidor);

        return services;
    }

    /// <summary>
    /// Liga o fato <typeparamref name="TEvento"/> de outro módulo ao handler que cria a notificação
    /// (<see cref="GeradorDeNotificacao{TEvento}"/>), consumido no Inbox do Configuração (idempotente).
    /// </summary>
    public static IServiceCollection AddGeradorDeNotificacao<TEvento, THandler>(this IServiceCollection services)
        where TEvento : IIntegrationEvent
        where THandler : GeradorDeNotificacao<TEvento> =>
        services.AddIntegrationEventHandler<TEvento, THandler, ConfiguracaoDbContext>(Consumidor);
}
