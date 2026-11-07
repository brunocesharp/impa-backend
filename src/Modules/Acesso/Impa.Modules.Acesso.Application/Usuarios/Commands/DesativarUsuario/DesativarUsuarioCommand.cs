using Ardalis.Result;
using MediatR;

namespace Impa.Modules.Acesso.Application.Usuarios.Commands.DesativarUsuario;

/// <summary>Desativa o usuário e encerra as sessões dele (cenário 1.4, RN-1.7).</summary>
public sealed record DesativarUsuarioCommand(Guid UsuarioId) : IRequest<Result>
{
    public const string MensagemNaoEncontrado = "Usuário não encontrado";
}
