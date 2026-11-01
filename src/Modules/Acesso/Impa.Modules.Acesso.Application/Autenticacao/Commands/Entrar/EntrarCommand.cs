using Ardalis.Result;
using MediatR;

namespace Impa.Modules.Acesso.Application.Autenticacao.Commands.Entrar;

/// <summary>Entrar com usuário e senha (cenários 1.1 e 1.2).</summary>
public sealed record EntrarCommand(string Usuario, string Senha) : IRequest<Result>
{
    public const string MensagemCredencialInvalida = "Usuário ou senha inválidos";

    // O ToString padrão do record incluiria a senha em qualquer log que serialize o command.
    public override string ToString() => $"{nameof(EntrarCommand)} {{ Usuario = {Usuario}, Senha = *** }}";
}
