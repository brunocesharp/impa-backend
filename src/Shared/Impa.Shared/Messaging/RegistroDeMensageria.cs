using Microsoft.EntityFrameworkCore;

namespace Impa.Shared.Messaging;

/// <summary>Um handler registrado: quem consome qual evento, em qual contexto (transação + Inbox).</summary>
public sealed record RegistroDeHandler(
    string Tipo,
    int Versao,
    Type ClasseDoEvento,
    Type ClasseDoHandler,
    Type ContextoDoConsumidor,
    string Consumidor,
    Func<object, IIntegrationEvent, CancellationToken, Task> Executar)
{
    /// <summary>Nome estável gravado no Inbox.</summary>
    public string Nome => ClasseDoHandler.FullName ?? ClasseDoHandler.Name;
}

/// <summary>
/// O que cada módulo registrou: contextos com Outbox (lidos pelo despachante) e handlers por tipo e versão.
/// Preenchido durante o registro dos serviços; só leitura depois.
/// </summary>
public sealed class RegistroDeMensageria
{
    private readonly List<Type> _outboxes = [];
    private readonly List<RegistroDeHandler> _handlers = [];

    public SerializadorDeEventos Serializador { get; } = new();

    public IReadOnlyList<Type> Outboxes => _outboxes;

    public IReadOnlyList<RegistroDeHandler> Handlers => _handlers;

    public IEnumerable<RegistroDeHandler> HandlersPara(string tipo, int versao) =>
        _handlers.Where(h => h.Tipo == tipo && h.Versao == versao);

    public IEnumerable<RegistroDeHandler> HandlersDoConsumidor(string consumidor) =>
        _handlers.Where(h => h.Consumidor == consumidor);

    internal void AdicionarOutbox(Type contexto)
    {
        if (!typeof(DbContext).IsAssignableFrom(contexto))
            throw new InvalidOperationException($"{contexto.FullName} não é um DbContext.");
        if (!_outboxes.Contains(contexto))
            _outboxes.Add(contexto);
    }

    internal void AdicionarHandler<TEvento, THandler>(Type contexto, string consumidor)
        where TEvento : IIntegrationEvent
        where THandler : IIntegrationEventHandler<TEvento>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumidor);
        if (_handlers.Any(h => h.ClasseDoHandler == typeof(THandler) && h.ClasseDoEvento == typeof(TEvento)))
            throw new InvalidOperationException($"{typeof(THandler).FullName} já registrado para {typeof(TEvento).Name}.");

        Serializador.Registrar(typeof(TEvento));
        var atributo = EventoDeIntegracaoAttribute.De(typeof(TEvento));
        _handlers.Add(new RegistroDeHandler(
            atributo.Tipo,
            atributo.Versao,
            typeof(TEvento),
            typeof(THandler),
            contexto,
            consumidor,
            (handler, evento, ct) => ((THandler)handler).HandleAsync((TEvento)evento, ct)));
    }
}
