using Microsoft.EntityFrameworkCore;

namespace Impa.Shared.Messaging;

internal sealed class EventPublisher<TContexto>(TContexto contexto, SerializadorDeEventos serializador, TimeProvider relogio)
    : IEventPublisher<TContexto> where TContexto : DbContext
{
    public void Publicar(IIntegrationEvent evento)
    {
        ArgumentNullException.ThrowIfNull(evento);
        if (contexto.Model.FindEntityType(typeof(OutboxMensagem)) is null)
            throw new InvalidOperationException(
                $"{typeof(TContexto).Name} não mapeia o Outbox: chame modelBuilder.MapearOutbox(schema) no OnModelCreating.");

        contexto.Set<OutboxMensagem>().Add(OutboxMensagem.Nova(evento, serializador.Serializar(evento), relogio.GetUtcNow()));
    }
}
