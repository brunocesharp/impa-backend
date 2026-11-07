using Impa.Shared.Messaging;

namespace Impa.Modules.Configuracao.Contracts.Notificacoes;

/// <summary>
/// Uma notificação foi criada no módulo Configuração (story 4.2). Consumido pelo e-mail (story 4.3), que manda a
/// cópia aos destinatários com a preferência ligada. <see cref="TipoDaNotificacao"/> é o tipo da notificação
/// (ex.: <c>estoque.saldo-negativo</c>), não o tipo do evento.
/// </summary>
[EventoDeIntegracao("configuracao.notificacao-criada", 1)]
public sealed record NotificacaoCriada(
    Guid NotificacaoId,
    string TipoDaNotificacao,
    string Titulo,
    string Texto,
    IReadOnlyList<Guid> Destinatarios) : IntegrationEvent
{
    public override string? ChaveDeCorrelacao => $"notificacao:{NotificacaoId}";
}
