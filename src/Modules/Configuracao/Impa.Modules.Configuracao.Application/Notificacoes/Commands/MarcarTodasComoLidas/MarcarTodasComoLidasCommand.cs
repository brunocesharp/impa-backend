using Ardalis.Result;
using MediatR;

namespace Impa.Modules.Configuracao.Application.Notificacoes.Commands.MarcarTodasComoLidas;

/// <summary>"Marcar todas": todas as notificações do usuário da sessão ficam lidas, só para ele.</summary>
public sealed record MarcarTodasComoLidasCommand(Guid UsuarioId) : IRequest<Result>;

public sealed class MarcarTodasComoLidasCommandHandler(INotificacoes notificacoes, TimeProvider relogio)
    : IRequestHandler<MarcarTodasComoLidasCommand, Result>
{
    public async Task<Result> Handle(MarcarTodasComoLidasCommand request, CancellationToken ct)
    {
        await notificacoes.MarcarTodasComoLidasAsync(request.UsuarioId, relogio.GetUtcNow(), ct);
        await notificacoes.SalvarAsync(ct);
        return Result.Success();
    }
}
