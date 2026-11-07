using Ardalis.Result;
using MediatR;

namespace Impa.Modules.Acesso.Application.Usuarios.Commands.DesativarUsuario;

public sealed class DesativarUsuarioCommandHandler(IGestaoUsuarios usuarios) : IRequestHandler<DesativarUsuarioCommand, Result>
{
    public async Task<Result> Handle(DesativarUsuarioCommand request, CancellationToken ct)
    {
        if (!await usuarios.ExisteAsync(request.UsuarioId, ct))
            return Result.NotFound(DesativarUsuarioCommand.MensagemNaoEncontrado);

        // Não desativar o último administrador é regra da story 1.3.
        await usuarios.DesativarAsync(request.UsuarioId, ct);
        return Result.Success();
    }
}
