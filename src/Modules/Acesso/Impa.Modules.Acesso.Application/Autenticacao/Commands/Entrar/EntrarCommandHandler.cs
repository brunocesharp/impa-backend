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

        // A senha é conferida antes de olhar a situação da conta: conta bloqueada ou desativada gasta o mesmo
        // hash, e o tempo de resposta não as separa das outras (story 1-1, revisão 3, R24).
        var senhaConfere = await sessao.SenhaConfereAsync(usuarioId.Value, request.Senha, ct);
        var situacao = await sessao.ObterSituacaoAsync(usuarioId.Value, ct);

        // RN-1.7: desativado não entra. Mesma resposta da senha errada, e não conta tentativa.
        if (!situacao.Ativo)
            return CredencialInvalida();

        // RN-1.2: durante o bloqueio, nem a senha correta entra, e o erro não renova o bloqueio.
        if (situacao.Bloqueada)
            return Bloqueado();

        if (!senhaConfere)
            return await sessao.RegistrarFalhaAsync(usuarioId.Value, ct) ? Bloqueado() : CredencialInvalida();

        // A situação lida acima pode ter mudado: senhas erradas em paralelo podem ter bloqueado a conta (R2).
        if (!await sessao.ZerarFalhasAsync(usuarioId.Value, ct))
            return Bloqueado();

        await sessao.AbrirAsync(usuarioId.Value, ct);
        return Result.Success();
    }

    // Mesma resposta para usuário inexistente e senha errada (cenário 1.2).
    private static Result CredencialInvalida() => Result.Unauthorized(EntrarCommand.MensagemCredencialInvalida);

    // A 5ª senha errada já responde com o bloqueio (decisão do usuário em 06/11/2026).
    private static Result Bloqueado() => Result.Unauthorized(EntrarCommand.MensagemBloqueado);
}
