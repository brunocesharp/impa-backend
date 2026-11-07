using System.Security.Claims;
using Impa.Modules.Acesso.Application.Autenticacao;
using Impa.Modules.Acesso.Application.Perfis;
using Impa.Modules.Acesso.Contracts;
using Impa.Modules.Acesso.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Impa.Modules.Acesso.Infrastructure.Identity;

public sealed class GestaoPerfis(RoleManager<Perfil> roleManager, AcessoDbContext banco, IPermissoesDoUsuario permissoes)
    : IGestaoPerfis
{
    public async Task<IReadOnlyList<PerfilDto>> ListarAsync(CancellationToken ct)
    {
        var perfis = await banco.Roles.AsNoTracking()
            .OrderByDescending(r => r.Protegido).ThenBy(r => r.Name)
            .Select(r => new { r.Id, r.Name, r.Protegido })
            .ToListAsync(ct);

        var claims = (await banco.RoleClaims.AsNoTracking()
                .Where(c => c.ClaimType == Permissoes.ClaimType)
                .Select(c => new { c.RoleId, Codigo = c.ClaimValue! })
                .ToListAsync(ct))
            .ToLookup(c => c.RoleId, c => c.Codigo);

        var usuarios = await banco.UserRoles.AsNoTracking()
            .GroupBy(ur => ur.RoleId)
            .Select(g => new { RoleId = g.Key, Total = g.Count() })
            .ToDictionaryAsync(g => g.RoleId, g => g.Total, ct);

        return perfis
            .Select(p => new PerfilDto(p.Id, p.Name ?? string.Empty, p.Protegido,
                Ordenar(claims[p.Id]), usuarios.GetValueOrDefault(p.Id)))
            .ToList();
    }

    public async Task<PerfilDto?> ObterAsync(Guid id, CancellationToken ct)
    {
        var perfil = await banco.Roles.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct);
        if (perfil is null)
            return null;

        var codigos = await banco.RoleClaims.AsNoTracking()
            .Where(c => c.RoleId == id && c.ClaimType == Permissoes.ClaimType)
            .Select(c => c.ClaimValue!)
            .ToListAsync(ct);
        var usuarios = await banco.UserRoles.CountAsync(ur => ur.RoleId == id, ct);

        return new PerfilDto(perfil.Id, perfil.Name ?? string.Empty, perfil.Protegido, Ordenar(codigos), usuarios);
    }

    public async Task<bool> NomeEmUsoAsync(string nome, Guid? exceto, CancellationToken ct)
    {
        var normalizado = roleManager.NormalizeKey(nome);
        return await banco.Roles.AnyAsync(r => r.NormalizedName == normalizado && r.Id != exceto, ct);
    }

    public async Task<PerfilDto> CriarAsync(string nome, IReadOnlyCollection<string> permissoesDoPerfil, CancellationToken ct)
    {
        await using var transacao = await banco.Database.BeginTransactionAsync(ct);

        var perfil = new Perfil(nome);
        Verificar(await roleManager.CreateAsync(perfil), "criar o perfil");
        await SincronizarPermissoesAsync(perfil, permissoesDoPerfil);

        await transacao.CommitAsync(ct);
        return (await ObterAsync(perfil.Id, ct))!;
    }

    public async Task<PerfilDto> AlterarAsync(Guid id, string nome, IReadOnlyCollection<string> permissoesDoPerfil, CancellationToken ct)
    {
        await using var transacao = await banco.Database.BeginTransactionAsync(ct);

        var perfil = await roleManager.FindByIdAsync(id.ToString())
            ?? throw new InvalidOperationException($"Perfil {id} não encontrado ao alterar.");
        if (perfil.Protegido)
            throw new InvalidOperationException("O perfil protegido não pode ser alterado."); // o handler já recusa

        if (perfil.Name != nome)
            Verificar(await roleManager.SetRoleNameAsync(perfil, nome), "renomear o perfil");
        Verificar(await roleManager.UpdateAsync(perfil), "alterar o perfil");
        await SincronizarPermissoesAsync(perfil, permissoesDoPerfil);

        await transacao.CommitAsync(ct);
        permissoes.Invalidar(); // depois do commit: uma leitura no meio não guarda o estado antigo
        return (await ObterAsync(id, ct))!;
    }

    public async Task ExcluirAsync(Guid id, CancellationToken ct)
    {
        var perfil = await roleManager.FindByIdAsync(id.ToString())
            ?? throw new InvalidOperationException($"Perfil {id} não encontrado ao excluir.");
        if (perfil.Protegido)
            throw new InvalidOperationException("O perfil protegido não pode ser excluído."); // o handler já recusa

        Verificar(await roleManager.DeleteAsync(perfil), "excluir o perfil");
        permissoes.Invalidar();
    }

    private async Task SincronizarPermissoesAsync(Perfil perfil, IReadOnlyCollection<string> desejadas)
    {
        var atuais = (await roleManager.GetClaimsAsync(perfil)).Where(c => c.Type == Permissoes.ClaimType).ToList();

        foreach (var sobrando in atuais.Where(c => !desejadas.Contains(c.Value)))
            Verificar(await roleManager.RemoveClaimAsync(perfil, sobrando), $"remover {sobrando.Value}");

        foreach (var faltando in desejadas.Where(p => atuais.All(c => c.Value != p)))
            Verificar(await roleManager.AddClaimAsync(perfil, new Claim(Permissoes.ClaimType, faltando)), $"dar {faltando}");
    }

    private static List<string> Ordenar(IEnumerable<string> codigos) =>
        codigos.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

    private static void Verificar(IdentityResult resultado, string acao)
    {
        if (!resultado.Succeeded)
            throw new InvalidOperationException(
                $"Não foi possível {acao}: {string.Join("; ", resultado.Errors.Select(e => e.Description))}");
    }
}
