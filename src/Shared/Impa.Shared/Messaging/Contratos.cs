using Microsoft.EntityFrameworkCore;

namespace Impa.Shared.Messaging;

/// <summary>
/// Publica integration events no Outbox do módulo dono de <typeparamref name="TContexto"/> (ADR-011):
/// o evento é só adicionado ao contexto e vai para o banco no mesmo <c>SaveChanges</c>/transação dos dados.
/// Se a transação for desfeita, nada é publicado.
/// </summary>
public interface IEventPublisher<TContexto> where TContexto : DbContext
{
    void Publicar(IIntegrationEvent evento);
}

/// <summary>
/// Handler de um módulo consumidor. Roda numa transação do contexto do consumidor, que grava o Inbox
/// junto com o efeito: deve usar o contexto do próprio módulo (o mesmo do registro) e não precisa chamar
/// <c>SaveChanges</c>. Não deve ter efeito fora do banco do módulo sem ser idempotente.
/// </summary>
public interface IIntegrationEventHandler<in TEvento> where TEvento : IIntegrationEvent
{
    Task HandleAsync(TEvento evento, CancellationToken ct);
}

/// <summary>Espera pelos eventos pendentes de uma chave antes de uma decisão síncrona (ADR-012, regra 5).</summary>
public interface IAguardarEventos
{
    /// <summary>
    /// Retorna <c>true</c> quando não há eventos com a <paramref name="chave"/> pendentes para o
    /// <paramref name="consumidor"/> (todos os handlers dele já processaram), ou <c>false</c> se o
    /// <paramref name="timeout"/> vencer antes. Evento "com falha" continua pendente.
    /// </summary>
    /// <exception cref="ArgumentException">O consumidor não tem handler registrado.</exception>
    Task<bool> AguardarAsync(string chave, string consumidor, TimeSpan timeout, CancellationToken ct = default);
}
