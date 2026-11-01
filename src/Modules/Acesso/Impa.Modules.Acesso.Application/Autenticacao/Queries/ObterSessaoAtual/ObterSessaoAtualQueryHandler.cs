using Ardalis.Result;
using MediatR;

namespace Impa.Modules.Acesso.Application.Autenticacao.Queries.ObterSessaoAtual;

public sealed class ObterSessaoAtualQueryHandler(ISessaoService sessao)
    : IRequestHandler<ObterSessaoAtualQuery, Result<SessaoDto>>
{
    public async Task<Result<SessaoDto>> Handle(ObterSessaoAtualQuery request, CancellationToken ct)
    {
        var atual = await sessao.ObterAtualAsync(ct);
        return atual is null ? Result<SessaoDto>.Unauthorized() : Result.Success(atual);
    }
}
