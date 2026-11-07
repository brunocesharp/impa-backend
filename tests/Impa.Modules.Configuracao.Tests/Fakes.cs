using Impa.Modules.Acesso.Contracts;
using Impa.Modules.Configuracao.Application.Notificacoes;
using Impa.Modules.Configuracao.Domain.Notificacoes;
using Impa.Shared.Messaging;

namespace Impa.Modules.Configuracao.Tests;

/// <summary>Usuários e permissões em memória, como o Acesso responderia (só os ativos).</summary>
public sealed class UsuariosComPermissaoFake : IUsuariosComPermissao
{
    public Dictionary<string, List<Guid>> PorPermissao { get; } = new();

    public List<string> Consultadas { get; } = [];

    public Task<IReadOnlyList<Guid>> ListarAtivosComPermissaoAsync(string permissao, CancellationToken ct)
    {
        Consultadas.Add(permissao);
        return Task.FromResult<IReadOnlyList<Guid>>(PorPermissao.GetValueOrDefault(permissao) ?? []);
    }
}

public sealed class EventosFake : IEventosDaConfiguracao
{
    public List<IIntegrationEvent> Publicados { get; } = [];

    public void Publicar(IIntegrationEvent evento) => Publicados.Add(evento);
}

/// <summary>Repositório em memória com a mesma semântica do da Infrastructure.</summary>
public sealed class NotificacoesFake : INotificacoes
{
    public List<Notificacao> Todas { get; } = [];

    public int Salvamentos { get; private set; }

    public void Adicionar(Notificacao notificacao) => Todas.Add(notificacao);

    public Task<Notificacao?> ObterAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(Todas.SingleOrDefault(n => n.Id == id));

    public Task<PaginaDeNotificacoesDto> ListarDoUsuarioAsync(Guid usuarioId, int pagina, int tamanhoDaPagina, CancellationToken ct)
    {
        var doUsuario = Todas.Where(n => n.EhDestinatario(usuarioId)).OrderByDescending(n => n.CriadaEm).ToList();
        var itens = doUsuario.Skip((pagina - 1) * tamanhoDaPagina).Take(tamanhoDaPagina)
            .Select(n => new NotificacaoDto(n.Id, n.Tipo, n.Titulo, n.Texto, n.ReferenciaDaOrigem, n.CriadaEm, n.LidaPor(usuarioId)))
            .ToList();
        return Task.FromResult(new PaginaDeNotificacoesDto(itens, pagina, tamanhoDaPagina, doUsuario.Count));
    }

    public Task<int> ContarNaoLidasAsync(Guid usuarioId, CancellationToken ct) =>
        Task.FromResult(Todas.Count(n => n.EhDestinatario(usuarioId) && !n.LidaPor(usuarioId)));

    public Task MarcarTodasComoLidasAsync(Guid usuarioId, DateTimeOffset agora, CancellationToken ct)
    {
        foreach (var n in Todas.Where(n => n.EhDestinatario(usuarioId)))
            n.MarcarComoLida(usuarioId, agora);
        return Task.CompletedTask;
    }

    public Task SalvarAsync(CancellationToken ct)
    {
        Salvamentos++;
        return Task.CompletedTask;
    }
}

public sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => agora;
}
