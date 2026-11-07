using Ardalis.Result;
using MediatR;

namespace Impa.Modules.Acesso.Application.Usuarios.Queries.ListarUsuarios;

/// <summary>Lista da tela Usuários (story 1.4): perfil e situação de cada um.</summary>
public sealed record ListarUsuariosQuery : IRequest<Result<IReadOnlyList<UsuarioDto>>>;

public sealed class ListarUsuariosQueryHandler(ICadastroUsuarios cadastro)
    : IRequestHandler<ListarUsuariosQuery, Result<IReadOnlyList<UsuarioDto>>>
{
    public async Task<Result<IReadOnlyList<UsuarioDto>>> Handle(ListarUsuariosQuery request, CancellationToken ct) =>
        Result.Success(await cadastro.ListarAsync(ct));
}
