using Impa.Modules.Configuracao.Domain.Notificacoes;
using Impa.Shared.Messaging;

namespace Impa.Modules.Configuracao.Application.Notificacoes;

/// <summary>Uma notificação vista pelo usuário da sessão: <paramref name="Lida"/> é a leitura <b>dele</b>.</summary>
public sealed record NotificacaoDto(
    Guid Id, string Tipo, string Titulo, string Texto, string? ReferenciaDaOrigem, DateTimeOffset CriadaEm, bool Lida);

/// <summary>Página da lista do usuário, mais recentes primeiro. <paramref name="Total"/>: todas as dele.</summary>
public sealed record PaginaDeNotificacoesDto(IReadOnlyList<NotificacaoDto> Itens, int Pagina, int TamanhoDaPagina, int Total);

public sealed record ContagemDeNaoLidasDto(int NaoLidas);

/// <summary>Notificações do módulo Configuração (schema <c>configuracao</c>); implementado na Infrastructure.</summary>
public interface INotificacoes
{
    void Adicionar(Notificacao notificacao);

    /// <summary>Com destinatários e leituras.</summary>
    Task<Notificacao?> ObterAsync(Guid id, CancellationToken ct);

    /// <summary>Só as notificações de que o usuário é destinatário.</summary>
    Task<PaginaDeNotificacoesDto> ListarDoUsuarioAsync(Guid usuarioId, int pagina, int tamanhoDaPagina, CancellationToken ct);

    Task<int> ContarNaoLidasAsync(Guid usuarioId, CancellationToken ct);

    /// <summary>Marca como lidas, para o usuário, todas as dele ainda não lidas (grava em <see cref="SalvarAsync"/>).</summary>
    Task MarcarTodasComoLidasAsync(Guid usuarioId, DateTimeOffset agora, CancellationToken ct);

    Task SalvarAsync(CancellationToken ct);
}

/// <summary>Publica integration events no Outbox do módulo Configuração, na mesma transação dos dados (ADR-011).</summary>
public interface IEventosDaConfiguracao
{
    void Publicar(IIntegrationEvent evento);
}
