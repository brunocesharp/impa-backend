namespace Impa.Shared.Messaging;

/// <summary>
/// Integration event (ADR-011): fato já confirmado por um módulo (ADR-012, regra 4), publicado no Outbox
/// do próprio módulo na mesma transação dos dados e entregue uma vez a cada consumidor.
/// Contrato público e versionado do módulo publicador (<c>Impa.Modules.{Modulo}.Contracts</c>).
/// </summary>
public interface IIntegrationEvent
{
    /// <summary>Identificador único: chave do Outbox e do Inbox (idempotência).</summary>
    Guid Id { get; }

    /// <summary>Quando o fato aconteceu (UTC).</summary>
    DateTimeOffset OcorreuEm { get; }

    /// <summary>Nome estável do evento, ex.: <c>vendas.venda-finalizada</c>.</summary>
    string Tipo { get; }

    /// <summary>Versão do contrato do evento; muda quando o conteúdo muda de forma incompatível.</summary>
    int Versao { get; }

    /// <summary>
    /// Chave do dado afetado (ex.: <c>caixa:{id}</c>, <c>produto:{id}</c>), usada para aguardar os eventos
    /// pendentes daquele dado antes de uma decisão síncrona (ADR-012, regra 5). Opcional.
    /// </summary>
    string? ChaveDeCorrelacao { get; }
}
