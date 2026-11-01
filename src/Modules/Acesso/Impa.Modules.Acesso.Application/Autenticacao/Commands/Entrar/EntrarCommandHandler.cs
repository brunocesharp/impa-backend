using Ardalis.Result;
using MediatR;

namespace Impa.Modules.Acesso.Application.Autenticacao.Commands.Entrar;

public sealed class EntrarCommandHandler(ISessaoService sessao) : IRequestHandler<EntrarCommand, Result>
{
    public async Task<Result> Handle(EntrarCommand request, CancellationToken ct)
    {
        // Mesma resposta para usuário inexistente e senha errada (cenário 1.2).
        return await sessao.EntrarAsync(request.Usuario, request.Senha, ct)
            ? Result.Success()
            : Result.Unauthorized(EntrarCommand.MensagemCredencialInvalida);
    }
}
