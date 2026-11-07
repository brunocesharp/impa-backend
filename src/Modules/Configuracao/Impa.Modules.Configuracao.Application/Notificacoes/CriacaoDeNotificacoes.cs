using Impa.Modules.Acesso.Contracts;
using Impa.Modules.Configuracao.Contracts.Notificacoes;
using Impa.Modules.Configuracao.Domain.Notificacoes;

namespace Impa.Modules.Configuracao.Application.Notificacoes;

/// <summary>O que um fato vira na central: tipo, título, texto e a referência do dado de origem.</summary>
public sealed record NovaNotificacao(string Tipo, string Titulo, string Texto, string? ReferenciaDaOrigem);

/// <summary>
/// Cria a notificação para os usuários <b>ativos</b> que têm a permissão de recebê-las <b>agora</b> (RN-2.3) e publica
/// <see cref="NotificacaoCriada"/> (e-mail, story 4.3). Não grava: roda dentro da transação do handler do fato, que o
/// despachante grava junto com o Inbox (idempotência, ADR-011).
/// </summary>
public sealed class CriacaoDeNotificacoes(
    INotificacoes notificacoes, IUsuariosComPermissao usuarios, IEventosDaConfiguracao eventos, TimeProvider relogio)
{
    public async Task<Notificacao> CriarAsync(NovaNotificacao nova, string permissao, CancellationToken ct)
    {
        var destinatarios = await usuarios.ListarAtivosComPermissaoAsync(permissao, ct);
        var notificacao = Notificacao.Criar(
            nova.Tipo, nova.Titulo, nova.Texto, nova.ReferenciaDaOrigem, relogio.GetUtcNow(), destinatarios);

        notificacoes.Adicionar(notificacao);
        eventos.Publicar(new NotificacaoCriada(
            notificacao.Id, notificacao.Tipo, notificacao.Titulo, notificacao.Texto,
            notificacao.Destinatarios.Select(d => d.UsuarioId).ToList()));
        return notificacao;
    }
}
