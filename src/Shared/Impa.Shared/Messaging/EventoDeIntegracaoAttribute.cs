using System.Collections.Concurrent;
using System.Reflection;

namespace Impa.Shared.Messaging;

/// <summary>Tipo e versão de um integration event, gravados no Outbox junto com o conteúdo JSON.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EventoDeIntegracaoAttribute(string tipo, int versao) : Attribute
{
    public string Tipo { get; } = tipo;

    public int Versao { get; } = versao;

    private static readonly ConcurrentDictionary<Type, EventoDeIntegracaoAttribute> PorClasse = new();

    /// <summary>Atributo da classe do evento; sem ele o evento não pode ser publicado nem consumido.</summary>
    public static EventoDeIntegracaoAttribute De(Type tipoDoEvento) =>
        PorClasse.GetOrAdd(tipoDoEvento, t =>
            t.GetCustomAttribute<EventoDeIntegracaoAttribute>()
            ?? throw new InvalidOperationException(
                $"O evento {t.FullName} não tem [{nameof(EventoDeIntegracaoAttribute).Replace("Attribute", "")}(tipo, versão)]."));
}
