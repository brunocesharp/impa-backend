using System.Security.Claims;
using Impa.Modules.Acesso.Application.Autenticacao;
using Impa.Modules.Acesso.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace Impa.Modules.Acesso.Infrastructure.Identity;

/// <summary>Sessão por cookie do Identity (ADR-006). O cookie em si é configurado pelo Host.</summary>
public sealed class SessaoService(
    SignInManager<Usuario> signInManager,
    UserManager<Usuario> userManager,
    IHttpContextAccessor httpContextAccessor) : ISessaoService
{
    private HttpContext Contexto => httpContextAccessor.HttpContext
        ?? throw new InvalidOperationException("Sessão usada fora de uma requisição HTTP.");

    public async Task<bool> EntrarAsync(string usuario, string senha, CancellationToken ct)
    {
        var encontrado = await userManager.FindByNameAsync(usuario);
        if (encontrado is null)
            return false;

        // Bloqueio por tentativas (RN-1.2) é da story 1.2.
        var resultado = await signInManager.PasswordSignInAsync(encontrado, senha, isPersistent: false, lockoutOnFailure: false);
        if (!resultado.Succeeded)
            return false;

        // A requisição segue com o usuário que acabou de entrar: o token antiforgery emitido
        // na resposta precisa estar ligado a essa identidade, não à anônima.
        Contexto.User = await signInManager.CreateUserPrincipalAsync(encontrado);
        return true;
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
