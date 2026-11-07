using Ardalis.Result;
using Impa.Modules.Acesso.Application.Autenticacao;
using Impa.Modules.Acesso.Application.Usuarios;
using Impa.Modules.Acesso.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Impa.Modules.Acesso.Infrastructure.Identity;

public sealed class GestaoUsuarios(
    UserManager<Usuario> userManager,
    AcessoDbContext banco,
    TimeProvider relogio,
    IPermissoesDoUsuario permissoes) : IGestaoUsuarios
{
    /// <summary>Chave da trava (advisory lock do PostgreSQL) sobre "quem é administrador ativo".</summary>
    private const long ChaveDaTravaDeAdministradores = 0x494D50415F41444D; // "IMPA_ADM"

    public Task<bool> ExisteAsync(Guid usuarioId, CancellationToken ct) =>
        banco.Users.AnyAsync(u => u.Id == usuarioId, ct);

    private IQueryable<Guid> AdministradoresAtivos =>
        from u in banco.Users
        join ur in banco.UserRoles on u.Id equals ur.UserId
        join r in banco.Roles on ur.RoleId equals r.Id
        where u.Ativo && r.Protegido
        select u.Id;

    public Task<bool> EhAdministradorAtivoAsync(Guid usuarioId, CancellationToken ct) =>
        AdministradoresAtivos.AnyAsync(id => id == usuarioId, ct);

    public Task<int> ContarAdministradoresAtivosAsync(CancellationToken ct) =>
        AdministradoresAtivos.Distinct().CountAsync(ct);

    public async Task<Result> ComAdministradoresTravadosAsync(Func<Task<Result>> operacao, CancellationToken ct)
    {
        await using var transacao = await banco.Database.BeginTransactionAsync(ct);

        // Até o commit, outra alteração de administradores espera aqui; a contagem lida depois da trava é a atual.
        await banco.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({ChaveDaTravaDeAdministradores})", ct);

        var resultado = await operacao();
        if (!resultado.IsSuccess)
            return resultado; // o dispose desfaz a transação

        await transacao.CommitAsync(ct);
        permissoes.Invalidar(); // perfil trocado: vale na próxima requisição
        return resultado;
    }

    public async Task DesativarAsync(Guid usuarioId, CancellationToken ct)
    {
        // O UserManager usa este mesmo AcessoDbContext (escopo da requisição): as três gravações vão juntas.
        // Chamado de dentro de ComAdministradoresTravadosAsync, usa a transação de lá.
        var propria = banco.Database.CurrentTransaction is null
            ? await banco.Database.BeginTransactionAsync(ct)
            : null;
        await using var _ = propria;

        var usuario = await userManager.FindByIdAsync(usuarioId.ToString())
            ?? throw new InvalidOperationException($"Usuário {usuarioId} não encontrado ao desativar.");

        usuario.Desativar();

        // Grava o Ativo = false e troca o stamp: um tíquete que escapasse da revogação cai na validação do stamp.
        var resultado = await userManager.UpdateSecurityStampAsync(usuario);
        if (!resultado.Succeeded)
            throw new InvalidOperationException(
                $"Não foi possível desativar {usuarioId}: {string.Join("; ", resultado.Errors.Select(e => e.Description))}");

        // RN-1.7: as sessões abertas são encerradas — a próxima requisição com o cookie recebe 401.
        var agora = relogio.GetUtcNow();
        await banco.Sessoes
            .Where(s => s.UsuarioId == usuarioId && s.RevogadaEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevogadaEm, agora), ct);

        if (propria is not null)
            await propria.CommitAsync(ct);
    }

    public async Task TrocarPerfilAsync(Guid usuarioId, Guid perfilId, CancellationToken ct)
    {
        var usuario = await userManager.FindByIdAsync(usuarioId.ToString())
            ?? throw new InvalidOperationException($"Usuário {usuarioId} não encontrado ao trocar o perfil.");
        var perfil = await banco.Roles.SingleOrDefaultAsync(r => r.Id == perfilId, ct)
            ?? throw new InvalidOperationException($"Perfil {perfilId} não encontrado ao trocar o perfil.");

        var atuais = await userManager.GetRolesAsync(usuario);
        if (atuais.Count == 1 && atuais[0] == perfil.Name)
            return;

        if (atuais.Count > 0)
            Verificar(await userManager.RemoveFromRolesAsync(usuario, atuais), "tirar o perfil atual");
        Verificar(await userManager.AddToRoleAsync(usuario, perfil.Name!), "dar o perfil");
        permissoes.Invalidar();
    }

    private static void Verificar(IdentityResult resultado, string acao)
    {
        if (!resultado.Succeeded)
            throw new InvalidOperationException(
                $"Não foi possível {acao}: {string.Join("; ", resultado.Errors.Select(e => e.Description))}");
    }
}
