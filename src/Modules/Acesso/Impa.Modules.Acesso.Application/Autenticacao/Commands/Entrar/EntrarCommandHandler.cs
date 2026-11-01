using Ardalis.Result;
using MediatR;

namespace Impa.Modules.Acesso.Application.Autenticacao.Commands.Entrar;

public sealed class EntrarCommandHandler(ISessaoService sessao) : IRequestHandler<EntrarCommand, Result>
{
    public async Task<Result> Handle(EntrarCommand request, CancellationToken ct)
    {
        // Teclado do celular costuma deixar espaço no fim do login; a senha é conferida como digitada.
        var login = request.Usuario.Trim();

        var usuarioId = await sessao.BuscarUsuarioAsync(login, ct);
        if (usuarioId is null)
        {
            // Mesmo custo de uma senha errada: o tempo não revela que o login não existe.
            await sessao.ConferirSenhaFicticiaAsync(request.Senha, ct);
            return CredencialInvalida();
        }

        // Bloqueio por tentativas (RN-1.2) é da story 1.2.
        if (!await sessao.SenhaConfereAsync(usuarioId.Value, request.Senha, ct))
            return CredencialInvalida();

        await sessao.AbrirAsync(usuarioId.Value, ct);
        return Result.Success();
    }

    // Mesma resposta para usuário inexistente e senha errada (cenário 1.2).
    private static Result CredencialInvalida() => Result.Unauthorized(EntrarCommand.MensagemCredencialInvalida);
}
