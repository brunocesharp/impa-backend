using System.Security.Claims;
using Impa.Modules.Acesso.Application.Autenticacao;
using Impa.Modules.Acesso.Contracts;
using Impa.Modules.Acesso.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace Impa.Modules.Acesso.Infrastructure.Identity;

/// <summary>Primitivas de sessão por cookie do Identity (ADR-006). O cookie em si é configurado pelo Host.</summary>
public sealed class SessaoService(
    SignInManager<Usuario> signInManager,
    UserManager<Usuario> userManager,
    IPasswordHasher<Usuario> passwordHasher,
    HashFicticio hashFicticio,
    IHttpContextAccessor httpContextAccessor,
    AcessoDbContext banco,
    TimeProvider relogio) : ISessaoService
{
    private HttpContext Contexto => httpContextAccessor.HttpContext
        ?? throw new InvalidOperationException("Sessão usada fora de uma requisição HTTP.");

    public async Task<Guid?> BuscarUsuarioAsync(string login, CancellationToken ct) =>
        (await userManager.FindByNameAsync(login))?.Id;

    public async Task<bool> SenhaConfereAsync(Guid usuarioId, string senha, CancellationToken ct)
    {
        var usuario = await userManager.FindByIdAsync(usuarioId.ToString());
        if (usuario is null)
            return false;

        // Primitiva pura: sempre calcula o hash, sem olhar bloqueio nem se a conta pode entrar.
        // Essas decisões são do handler (ADR-013, regra 2), com ObterSituacaoAsync.
        if (usuario.PasswordHash is null)
        {
            // Conta sem senha: o UserManager recusaria sem PBKDF2 e o tempo separaria essa conta das outras.
            await ConferirSenhaFicticiaAsync(senha, ct);
            return false;
        }

        return await userManager.CheckPasswordAsync(usuario, senha);
    }

    public Task ConferirSenhaFicticiaAsync(string senha, CancellationToken ct)
    {
        var ficticio = new Usuario();
        passwordHasher.VerifyHashedPassword(ficticio, hashFicticio.Obter(passwordHasher), senha);
        return Task.CompletedTask;
    }

    public async Task<SituacaoDaConta> ObterSituacaoAsync(Guid usuarioId, CancellationToken ct)
    {
        var usuario = await ObterAsync(usuarioId);
        var bloqueada = usuario.LockoutEnabled && usuario.LockoutEnd > relogio.GetUtcNow();
        return new SituacaoDaConta(usuario.Ativo, bloqueada);
    }

    // R2 (revisão 1 da 1-2): a contagem é um UPDATE atômico, não o AccessFailedAsync do Identity (ler, somar,
    // gravar com ConcurrencyStamp). Senhas erradas em paralelo esperam o lock da linha e o WHERE é reavaliado
    // sobre a versão nova: depois do 5º erro, as demais já veem a conta bloqueada — sem 500 e sem passar de 5.
    // O ConcurrencyStamp muda, como numa gravação do Identity, para um UpdateAsync concorrente acusar conflito.

    public async Task<bool> RegistrarFalhaAsync(Guid usuarioId, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        var fimDoBloqueio = agora.Add(DependencyInjection.DuracaoDoBloqueio);
        var maximo = DependencyInjection.MaximoDeSenhasErradas;

        var bloqueouAgora = await banco.Database.SqlQuery<bool>($"""
            UPDATE acesso.usuarios SET
                "AccessFailedCount" = CASE WHEN "LockoutEnabled" AND "AccessFailedCount" + 1 >= {maximo}
                                           THEN 0 ELSE "AccessFailedCount" + 1 END,
                "LockoutEnd" = CASE WHEN "LockoutEnabled" AND "AccessFailedCount" + 1 >= {maximo}
                                    THEN {fimDoBloqueio} ELSE "LockoutEnd" END,
                "ConcurrencyStamp" = gen_random_uuid()::text
            WHERE "Id" = {usuarioId}
              AND NOT ("LockoutEnabled" AND "LockoutEnd" IS NOT NULL AND "LockoutEnd" > {agora})
            RETURNING ("LockoutEnabled" AND "LockoutEnd" IS NOT NULL AND "LockoutEnd" > {agora}) AS "Value"
            """).ToListAsync(ct);

        // Nenhuma linha: a conta já estava bloqueada (outra requisição chegou ao 5º erro antes).
        return bloqueouAgora.Count == 0 || bloqueouAgora[0];
    }

    public async Task<bool> ZerarFalhasAsync(Guid usuarioId, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();

        var zerou = await banco.Database.SqlQuery<bool>($"""
            UPDATE acesso.usuarios SET
                "AccessFailedCount" = 0,
                "ConcurrencyStamp" = CASE WHEN "AccessFailedCount" = 0
                                          THEN "ConcurrencyStamp" ELSE gen_random_uuid()::text END
            WHERE "Id" = {usuarioId}
              AND NOT ("LockoutEnabled" AND "LockoutEnd" IS NOT NULL AND "LockoutEnd" > {agora})
            RETURNING TRUE AS "Value"
            """).ToListAsync(ct);

        return zerou.Count > 0;
    }

    private async Task<Usuario> ObterAsync(Guid usuarioId) =>
        await userManager.FindByIdAsync(usuarioId.ToString())
        ?? throw new InvalidOperationException($"Usuário {usuarioId} não encontrado.");

    public async Task AbrirAsync(Guid usuarioId, CancellationToken ct)
    {
        var usuario = await ObterAsync(usuarioId);

        // Persistente: sobrevive a fechar o navegador/PWA, mas o tíquete continua limitado a 12 h sem
        // expiração deslizante (RN-1.3; decisão do usuário em 01/11/2026).
        await signInManager.SignInAsync(usuario, isPersistent: true);

        // A requisição segue com o usuário que acabou de entrar: o token antiforgery emitido
        // na resposta precisa estar ligado a essa identidade, não à anônima.
        Contexto.User = await signInManager.CreateUserPrincipalAsync(usuario);
    }

    public async Task SairAsync(CancellationToken ct)
    {
        await signInManager.SignOutAsync();
        Contexto.User = new ClaimsPrincipal(new ClaimsIdentity());
    }

    public async Task<SessaoDto?> ObterAtualAsync(CancellationToken ct)
    {
        var principal = Contexto.User;
        if (principal.Identity?.IsAuthenticated != true)
            return null;

        var usuario = await userManager.GetUserAsync(principal);
        if (usuario is null)
            return null;

        var permissoes = principal.FindAll(Permissoes.ClaimType).Select(c => c.Value).Distinct().Order().ToList();
        var perfil = principal.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

        return new SessaoDto(usuario.UserName!, usuario.NomeExibicao, perfil, permissoes);
    }
}
