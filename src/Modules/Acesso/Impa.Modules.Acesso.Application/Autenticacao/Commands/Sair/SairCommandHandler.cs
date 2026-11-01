using Ardalis.Result;
using MediatR;

namespace Impa.Modules.Acesso.Application.Autenticacao.Commands.Sair;

public sealed class SairCommandHandler(ISessaoService sessao) : IRequestHandler<SairCommand, Result>
{
    public async Task<Result> Handle(SairCommand request, CancellationToken ct)
    {
        await sessao.SairAsync(ct);
        return Result.Success();
    }
}
