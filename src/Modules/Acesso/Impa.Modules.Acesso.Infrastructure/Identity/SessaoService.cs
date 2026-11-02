using System.Security.Claims;
using Impa.Modules.Acesso.Application.Autenticacao;
using Impa.Modules.Acesso.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace Impa.Modules.Acesso.Infrastructure.Identity;

/// <summary>Primitivas de sessão por cookie do Identity (ADR-006). O cookie em si é configurado pelo Host.</summary>
public sealed class SessaoService(
    SignInManager<Usuario> signInManager,
    UserManager<Usuario> userManager,
    IPasswordHasher<Usuario> passwordHasher,
    HashFicticio hashFicticio,
    IHttpContextAccessor httpContextAccessor) : ISessaoService
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
        // Essas decisões são do handler (ADR-013, regra 2; bloqueio na story 1.2).
        return await userManager.CheckPasswordAsync(usuario, senha);
    }

    public Task ConferirSenhaFicticiaAsync(string senha, CancellationToken ct)
    {
        var ficticio = new Usuario();
        passwordHasher.VerifyHashedPassword(ficticio, hashFicticio.Obter(passwordHasher), senha);
        return Task.CompletedTask;
    }

    public async Task AbrirAsync(Guid usuarioId, CancellationToken ct)
    {
        var usuario = await userManager.FindByIdAsync(usuarioId.ToString())
            ?? throw new InvalidOperationException($"Usuário {usuarioId} não encontrado ao abrir a sessão.");

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
