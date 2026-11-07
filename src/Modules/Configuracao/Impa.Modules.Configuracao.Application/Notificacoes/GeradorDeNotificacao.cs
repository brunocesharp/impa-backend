using Impa.Modules.Acesso.Contracts;
using Impa.Shared.Messaging;

namespace Impa.Modules.Configuracao.Application.Notificacoes;

/// <summary>
/// Base dos handlers que transformam o <b>fato</b> de outro módulo (ADR-012) em notificação: o módulo de origem só
/// publica o evento (ex.: <c>EstoqueFicouNegativo</c>, story 7-2; <c>CaixaFechadoComDiferenca</c>, story 9-3) e o
/// Configuração tem um handler por tipo de fato. Cada story acrescenta só a subclasse e o registro:
/// <code>
/// public sealed class NotificarEstoqueNegativo(CriacaoDeNotificacoes criacao) : GeradorDeNotificacao&lt;EstoqueFicouNegativo&gt;(criacao)
/// {
///     protected override string Tipo => "estoque.saldo-negativo";
///     protected override string Titulo(EstoqueFicouNegativo e) => "Estoque negativo";
///     protected override string Texto(EstoqueFicouNegativo e) => $"{e.Item} ficou com saldo {e.Saldo}";
/// }
/// // Infrastructure do Configuração: services.AddGeradorDeNotificacao&lt;EstoqueFicouNegativo, NotificarEstoqueNegativo&gt;();
/// </code>
/// O despachante entrega o fato uma vez a este handler (Inbox do Configuração): reentrega não duplica a notificação.
/// </summary>
public abstract class GeradorDeNotificacao<TEvento>(CriacaoDeNotificacoes criacao) : IIntegrationEventHandler<TEvento>
    where TEvento : IIntegrationEvent
{
    /// <summary>Tipo estável da notificação (ex.: <c>estoque.saldo-negativo</c>).</summary>
    protected abstract string Tipo { get; }

    protected abstract string Titulo(TEvento evento);

    protected abstract string Texto(TEvento evento);

    /// <summary>Referência do dado de origem; por padrão, a chave de correlação do fato.</summary>
    protected virtual string? ReferenciaDaOrigem(TEvento evento) => evento.ChaveDeCorrelacao;

    /// <summary>Quem recebe: os usuários ativos com esta permissão no momento da criação.</summary>
    protected virtual string PermissaoDosDestinatarios => Permissoes.NotificacoesReceber;

    public Task HandleAsync(TEvento evento, CancellationToken ct) =>
        criacao.CriarAsync(
            new NovaNotificacao(Tipo, Titulo(evento), Texto(evento), ReferenciaDaOrigem(evento)),
            PermissaoDosDestinatarios, ct);
}
