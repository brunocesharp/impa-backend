using Impa.Modules.Acesso.Contracts;
using Impa.Modules.Acesso.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Impa.Modules.Acesso.Infrastructure.Identity;

/// <summary>Usuários ativos com a permissão no perfil atual, direto do banco (sem o cache das permissões da sessão).</summary>
public sealed class UsuariosComPermissao(AcessoDbContext banco) : IUsuariosComPermissao
{
    public async Task<IReadOnlyList<Guid>> ListarAtivosComPermissaoAsync(string permissao, CancellationToken ct)
    {
        if (!Permissoes.Existe(permissao))
            return [];

        return await (
                from u in banco.Users
                join ur in banco.UserRoles on u.Id equals ur.UserId
                join c in banco.RoleClaims on ur.RoleId equals c.RoleId
                where u.Ativo && c.ClaimType == Permissoes.ClaimType && c.ClaimValue == permissao
                select u.Id)
            .Distinct()
            .ToListAsync(ct);
    }
}
