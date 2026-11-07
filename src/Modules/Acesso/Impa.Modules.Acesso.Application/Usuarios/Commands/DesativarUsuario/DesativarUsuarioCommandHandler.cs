using Ardalis.Result;
using MediatR;

namespace Impa.Modules.Acesso.Application.Usuarios.Commands.DesativarUsuario;

public sealed class DesativarUsuarioCommandHandler(IGestaoUsuarios usuarios) : IRequestHandler<DesativarUsuarioCommand, Result>
{
    public async Task<Result> Handle(DesativarUsuarioCommand request, CancellationToken ct)
    {
        if (!await usuarios.ExisteAsync(request.UsuarioId, ct))
            return Result.NotFound(DesativarUsuarioCommand.MensagemNaoEncontrado);

        return await usuarios.ComAdministradoresTravadosAsync(async () =>
        {
            // Cenário 1.6 / RN-1.6: sempre um administrador ativo.
            if (await usuarios.EhAdministradorAtivoAsync(request.UsuarioId, ct)
                && await usuarios.ContarAdministradoresAtivosAsync(ct) <= 1)
                return Result.Conflict(MensagensUsuarios.UltimoAdministrador);

            await usuarios.DesativarAsync(request.UsuarioId, ct);
            return Result.Success();
        }, ct);
    }
}
