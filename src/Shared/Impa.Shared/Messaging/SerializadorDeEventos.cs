using System.Collections.Concurrent;
using System.Text.Json;

namespace Impa.Shared.Messaging;

/// <summary>
/// Serialização JSON versionada dos integration events: o Outbox guarda tipo, versão e conteúdo;
/// a leitura escolhe a classe registrada para aquele par (cada versão é uma classe própria).
/// </summary>
public sealed class SerializadorDeEventos
{
    private static readonly JsonSerializerOptions Opcoes = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<(string Tipo, int Versao), Type> _classes = new();

    /// <summary>Registra a classe de um evento. Duas classes com o mesmo tipo e versão são recusadas.</summary>
    public void Registrar(Type classeDoEvento)
    {
        if (!typeof(IIntegrationEvent).IsAssignableFrom(classeDoEvento))
            throw new InvalidOperationException($"{classeDoEvento.FullName} não implementa {nameof(IIntegrationEvent)}.");

        var atributo = EventoDeIntegracaoAttribute.De(classeDoEvento);
        var registrada = _classes.GetOrAdd((atributo.Tipo, atributo.Versao), classeDoEvento);
        if (registrada != classeDoEvento)
            throw new InvalidOperationException(
                $"Tipo '{atributo.Tipo}' v{atributo.Versao} já registrado por {registrada.FullName}; {classeDoEvento.FullName} repete o par.");
    }

    public bool Conhece(string tipo, int versao) => _classes.ContainsKey((tipo, versao));

    public string Serializar(IIntegrationEvent evento) =>
        JsonSerializer.Serialize(evento, evento.GetType(), Opcoes);

    public IIntegrationEvent Desserializar(string tipo, int versao, string conteudo)
    {
        if (!_classes.TryGetValue((tipo, versao), out var classe))
            throw new InvalidOperationException($"Evento '{tipo}' v{versao} sem classe registrada.");

        return (IIntegrationEvent)(JsonSerializer.Deserialize(conteudo, classe, Opcoes)
            ?? throw new InvalidOperationException($"Conteúdo vazio para o evento '{tipo}' v{versao}."));
    }
}
