using Impa.Modules.Configuracao.Application.Notificacoes;
using Impa.Modules.Configuracao.Domain.Notificacoes;
using Impa.Modules.Configuracao.Infrastructure.Persistence;
using Impa.Shared.Messaging;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Impa.Modules.Configuracao.Infrastructure.Notificacoes;

public sealed class RepositorioDeNotificacoes(ConfiguracaoDbContext banco) : INotificacoes
{
    public void Adicionar(Notificacao notificacao) => banco.Notificacoes.Add(notificacao);

    public Task<Notificacao?> ObterAsync(Guid id, CancellationToken ct) =>
        banco.Notificacoes
            .Include(n => n.Destinatarios)
            .Include(n => n.Leituras)
            .SingleOrDefaultAsync(n => n.Id == id, ct);

    public async Task<PaginaDeNotificacoesDto> ListarDoUsuarioAsync(
        Guid usuarioId, int pagina, int tamanhoDaPagina, CancellationToken ct)
    {
        var doUsuario = banco.Notificacoes.AsNoTracking()
            .Where(n => banco.NotificacoesDestinatarios.Any(d => d.NotificacaoId == n.Id && d.UsuarioId == usuarioId));

        var total = await doUsuario.CountAsync(ct);
        var itens = await doUsuario
            .OrderByDescending(n => n.CriadaEm).ThenByDescending(n => n.Id)
            .Skip((pagina - 1) * tamanhoDaPagina)
            .Take(tamanhoDaPagina)
            .Select(n => new NotificacaoDto(
                n.Id, n.Tipo, n.Titulo, n.Texto, n.ReferenciaDaOrigem, n.CriadaEm,
                banco.NotificacoesLidas.Any(l => l.NotificacaoId == n.Id && l.UsuarioId == usuarioId)))
            .ToListAsync(ct);

        return new PaginaDeNotificacoesDto(itens, pagina, tamanhoDaPagina, total);
    }

    public Task<int> ContarNaoLidasAsync(Guid usuarioId, CancellationToken ct) =>
        banco.NotificacoesDestinatarios.AsNoTracking()
            .CountAsync(d => d.UsuarioId == usuarioId
                && !banco.NotificacoesLidas.Any(l => l.NotificacaoId == d.NotificacaoId && l.UsuarioId == usuarioId), ct);

    public async Task MarcarTodasComoLidasAsync(Guid usuarioId, DateTimeOffset agora, CancellationToken ct)
    {
        var naoLidas = await banco.Notificacoes
            .Include(n => n.Destinatarios.Where(d => d.UsuarioId == usuarioId))
            .Include(n => n.Leituras.Where(l => l.UsuarioId == usuarioId))
            .Where(n => n.Destinatarios.Any(d => d.UsuarioId == usuarioId)
                && !n.Leituras.Any(l => l.UsuarioId == usuarioId))
            .ToListAsync(ct);

        foreach (var notificacao in naoLidas)
            notificacao.MarcarComoLida(usuarioId, agora);
    }

    /// <summary>
    /// Duas marcações da mesma notificação ao mesmo tempo (duplo clique, duas abas): a segunda bate na chave da
    /// leitura e é tratada como já lida.
    /// </summary>
    public async Task SalvarAsync(CancellationToken ct)
    {
        try
        {
            await banco.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }
                                          && e.Entries.All(x => x.Entity is NotificacaoLida))
        {
            banco.ChangeTracker.Clear();
        }
    }
}

/// <summary>Publica no Outbox do Configuração (vai ao banco no mesmo <c>SaveChanges</c> da notificação).</summary>
public sealed class EventosDaConfiguracao(IEventPublisher<ConfiguracaoDbContext> publicador) : IEventosDaConfiguracao
{
    public void Publicar(IIntegrationEvent evento) => publicador.Publicar(evento);
}
