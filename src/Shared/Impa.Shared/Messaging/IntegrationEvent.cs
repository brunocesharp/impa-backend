using System.Text.Json.Serialization;

namespace Impa.Shared.Messaging;

/// <summary>
/// Base dos integration events. O evento concreto é um <c>record</c> com
/// <see cref="EventoDeIntegracaoAttribute"/> (tipo e versão) e, quando houver, sobrescreve
/// <see cref="ChaveDeCorrelacao"/>:
/// <code>
/// [EventoDeIntegracao("vendas.venda-finalizada", 1)]
/// public sealed record VendaFinalizada(Guid VendaId, Guid CaixaId) : IntegrationEvent
/// {
///     public override string? ChaveDeCorrelacao => $"caixa:{CaixaId}";
/// }
/// </code>
/// </summary>
public abstract record IntegrationEvent : IIntegrationEvent
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public DateTimeOffset OcorreuEm { get; init; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public string Tipo => EventoDeIntegracaoAttribute.De(GetType()).Tipo;

    [JsonIgnore]
    public int Versao => EventoDeIntegracaoAttribute.De(GetType()).Versao;

    [JsonIgnore]
    public virtual string? ChaveDeCorrelacao => null;
}
