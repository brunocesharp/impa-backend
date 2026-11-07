using Ardalis.Result;
using Impa.Modules.Acesso.Application.Perfis;
using MediatR;

namespace Impa.Modules.Acesso.Application.Usuarios.Queries.ListarPerfisDosUsuarios;

/// <summary>
/// Perfis para escolher na tela Usuários (story 1.4, decisão A1 da revisão 1): quem tem <c>usuarios.gerenciar</c> e
/// não tem <c>perfis.gerenciar</c> precisa dos perfis para criar e editar usuários. Só id, nome e protegido.
/// </summary>
public sealed record ListarPerfisDosUsuariosQuery : IRequest<Result<IReadOnlyList<PerfilResumoDto>>>;

public sealed class ListarPerfisDosUsuariosQueryHandler(IGestaoPerfis perfis)
    : IRequestHandler<ListarPerfisDosUsuariosQuery, Result<IReadOnlyList<PerfilResumoDto>>>
{
    public async Task<Result<IReadOnlyList<PerfilResumoDto>>> Handle(ListarPerfisDosUsuariosQuery request, CancellationToken ct) =>
        Result.Success<IReadOnlyList<PerfilResumoDto>>(
            (await perfis.ListarAsync(ct)).Select(p => new PerfilResumoDto(p.Id, p.Nome, p.Protegido)).ToList());
}
