using Ardalis.Result;
using MediatR;

namespace Impa.Modules.Configuracao.Application.Notificacoes.Queries.ContarNaoLidas;

/// <summary>Contador do sino: notificações do usuário da sessão que ele ainda não leu.</summary>
public sealed record ContarNaoLidasQuery(Guid UsuarioId) : IRequest<Result<ContagemDeNaoLidasDto>>;

public sealed class ContarNaoLidasQueryHandler(INotificacoes notificacoes)
    : IRequestHandler<ContarNaoLidasQuery, Result<ContagemDeNaoLidasDto>>
{
    public async Task<Result<ContagemDeNaoLidasDto>> Handle(ContarNaoLidasQuery request, CancellationToken ct) =>
        Result.Success(new ContagemDeNaoLidasDto(await notificacoes.ContarNaoLidasAsync(request.UsuarioId, ct)));
}
