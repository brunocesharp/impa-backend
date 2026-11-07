using Ardalis.Result;
using Impa.Modules.Acesso.Application.Autenticacao;
using Impa.Modules.Acesso.Application.Usuarios;
using Impa.Modules.Acesso.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Impa.Modules.Acesso.Infrastructure.Identity;

public sealed class GestaoUsuarios(
    UserManager<Usuario> userManager,
    AcessoDbContext banco,
    TimeProvider relogio,
    IPermissoesDoUsuario permissoes) : IGestaoUsuarios, ICadastroUsuarios
{
    // ---- Cadastro (story 1.4) ----

    public async Task<IReadOnlyList<UsuarioDto>> ListarAsync(CancellationToken ct)
    {
        var usuarios = await banco.Users.AsNoTracking()
            .OrderBy(u => u.NomeExibicao).ThenBy(u => u.UserName)
            .Select(u => new { u.Id, u.UserName, u.NomeExibicao, u.Email, u.Ativo, u.TrocaDeSenhaObrigatoria })
            .ToListAsync(ct);

        var perfis = (await (
                from ur in banco.UserRoles.AsNoTracking()
                join r in banco.Roles on ur.RoleId equals r.Id
                select new { ur.UserId, r.Id, r.Name })
            .ToListAsync(ct))
            .ToLookup(p => p.UserId);

        return usuarios
            .Select(u =>
            {
                var perfil = perfis[u.Id].OrderBy(p => p.Name).FirstOrDefault(); // um perfil por usuário (RN-1.4)
                return new UsuarioDto(u.Id, u.UserName ?? string.Empty, u.NomeExibicao, u.Email,
                    perfil?.Id, perfil?.Name ?? string.Empty, u.Ativo, u.TrocaDeSenhaObrigatoria);
            })
            .ToList();
    }

    public async Task<UsuarioDto?> ObterAsync(Guid usuarioId, CancellationToken ct) =>
        (await ListarAsync(ct)).SingleOrDefault(u => u.Id == usuarioId);

    public Task<bool> LoginEmUsoAsync(string login, CancellationToken ct)
    {
        var normalizado = userManager.NormalizeName(login);
        return banco.Users.AnyAsync(u => u.NormalizedUserName == normalizado, ct);
    }

    public async Task<Guid?> CriarAsync(NovoUsuario dados, string senhaProvisoria, CancellationToken ct)
    {
        await using var transacao = await banco.Database.BeginTransactionAsync(ct);

        var perfil = await banco.Roles.SingleOrDefaultAsync(r => r.Id == dados.PerfilId, ct)
            ?? throw new InvalidOperationException($"Perfil {dados.PerfilId} não encontrado ao criar o usuário.");

        var usuario = new Usuario { UserName = dados.Login, NomeExibicao = dados.Nome, Email = dados.Email };
        usuario.ExigirTrocaDeSenha(); // RN-1.8: a senha inicial é provisória

        // Login tomado depois da conferência do handler (cadastro simultâneo): o validador do Identity devolve
        // DuplicateUserName ou, se os dois passaram juntos por ele, o índice único recusa o insert. Nos dois
        // casos nada fica gravado (o dispose desfaz a transação) e o handler responde 409.
        IdentityResult criado;
        try
        {
            criado = await userManager.CreateAsync(usuario, senhaProvisoria);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            banco.ChangeTracker.Clear();
            return null;
        }

        if (criado.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.DuplicateUserName)))
            return null;

        Verificar(criado, "criar o usuário");
        Verificar(await userManager.AddToRoleAsync(usuario, perfil.Name!), "dar o perfil");

        await transacao.CommitAsync(ct);
        return usuario.Id;
    }

    public async Task AlterarDadosAsync(Guid usuarioId, string nome, string? email, CancellationToken ct)
    {
        var usuario = await ObterEntidadeAsync(usuarioId, "alterar");
        usuario.NomeExibicao = nome;
        // Direto, e não pelo SetEmailAsync: ele troca o security stamp e derrubaria as sessões do usuário.
        usuario.Email = email;
        Verificar(await userManager.UpdateAsync(usuario), "alterar o usuário"); // também normaliza o e-mail
    }

    public async Task ReativarAsync(Guid usuarioId, CancellationToken ct)
    {
        var usuario = await ObterEntidadeAsync(usuarioId, "reativar");
        if (usuario.Ativo)
            return;

        usuario.Reativar();
        Verificar(await userManager.UpdateAsync(usuario), "reativar o usuário");
        permissoes.Invalidar(); // usuário inativo carrega sem permissões; ativo volta a ter as do perfil
    }

    public async Task DefinirSenhaProvisoriaAsync(Guid usuarioId, string senhaProvisoria, CancellationToken ct)
    {
        var usuario = await ObterEntidadeAsync(usuarioId, "redefinir a senha de");

        var erros = await ValidarSenhaAsync(userManager, usuario, senhaProvisoria);
        if (erros.Count > 0)
            throw new InvalidOperationException($"Senha provisória recusada: {string.Join("; ", erros)}");

        // Sem token de redefinição (o administrador não precisa dele): grava o hash e troca o stamp numa gravação só.
        usuario.PasswordHash = userManager.PasswordHasher.HashPassword(usuario, senhaProvisoria);
        usuario.ExigirTrocaDeSenha();
        // Decisão A3 da revisão 1: redefinir também desbloqueia (RN-1.2) — quem foi bloqueado por senhas erradas
        // entra com a provisória na hora, sem esperar os 15 minutos, e a contagem de falhas recomeça.
        usuario.AccessFailedCount = 0;
        usuario.LockoutEnd = null;
        Verificar(await userManager.UpdateSecurityStampAsync(usuario), "redefinir a senha");
        permissoes.Invalidar(); // a marca vale na próxima requisição de uma sessão já aberta
    }

    /// <summary>Roda os validadores de senha do Identity (RN-1.1). Vazio quando a senha é aceita.</summary>
    internal static async Task<IReadOnlyList<string>> ValidarSenhaAsync(UserManager<Usuario> userManager, Usuario usuario, string senha)
    {
        var erros = new List<string>();
        foreach (var validador in userManager.PasswordValidators)
        {
            var resultado = await validador.ValidateAsync(userManager, usuario, senha);
            erros.AddRange(resultado.Errors.Select(e => e.Description));
        }

        return erros;
    }

    private async Task<Usuario> ObterEntidadeAsync(Guid usuarioId, string acao) =>
        await userManager.FindByIdAsync(usuarioId.ToString())
        ?? throw new InvalidOperationException($"Usuário {usuarioId} não encontrado ao {acao}.");

    // ---- Desativação e perfil (stories 1-2 e 1-3) ----

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
