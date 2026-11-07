using Ardalis.Result;
using MediatR;

namespace Impa.Modules.Configuracao.Application.Notificacoes.Commands.MarcarComoLida;

/// <summary>O usuário da sessão marca uma notificação dele como lida, só para ele (cenário 2.5).</summary>
public sealed record MarcarComoLidaCommand(Guid NotificacaoId, Guid UsuarioId) : IRequest<Result>
{
    public const string MensagemNaoEncontrada = "Notificação não encontrada";
}

public sealed class MarcarComoLidaCommandHandler(INotificacoes notificacoes, TimeProvider relogio)
    : IRequestHandler<MarcarComoLidaCommand, Result>
{
    public async Task<Result> Handle(MarcarComoLidaCommand request, CancellationToken ct)
    {
        // Notificação de outro usuário responde como inexistente: não revela que ela existe.
        var notificacao = await notificacoes.ObterAsync(request.NotificacaoId, ct);
        if (notificacao is null || !notificacao.EhDestinatario(request.UsuarioId))
            return Result.NotFound(MarcarComoLidaCommand.MensagemNaoEncontrada);

        if (!notificacao.LidaPor(request.UsuarioId))
        {
            notificacao.MarcarComoLida(request.UsuarioId, relogio.GetUtcNow());
            await notificacoes.SalvarAsync(ct);
        }

        return Result.Success();
    }
}
