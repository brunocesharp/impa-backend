using Ardalis.Result;
using MediatR;

namespace Impa.Modules.Acesso.Application.Terminais.Commands.RevogarTerminal;

/// <summary>Revoga o terminal: o dispositivo deixa de acessar PDV e caixa na próxima requisição. Revogar de novo não muda nada.</summary>
public sealed record RevogarTerminalCommand(Guid Id) : IRequest<Result>;

public sealed class RevogarTerminalCommandHandler(ITerminais terminais) : IRequestHandler<RevogarTerminalCommand, Result>
{
    public async Task<Result> Handle(RevogarTerminalCommand request, CancellationToken ct)
    {
        if (await terminais.ObterAsync(request.Id, ct) is null)
            return Result.NotFound(MensagensTerminais.NaoEncontrado);

        await terminais.RevogarAsync(request.Id, ct);
        return Result.Success();
    }
}
