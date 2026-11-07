using Impa.Modules.Acesso.Application.Autenticacao;
using Impa.Modules.Acesso.Application.Autenticacao.Commands.TrocarSenha;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace Impa.Modules.Acesso.Infrastructure.Identity;

/// <summary>Troca de senha do usuário da sessão (story 1.4), com o Identity e o cookie da ADR-006.</summary>
public sealed class TrocaDeSenha(
    UserManager<Usuario> userManager,
    SignInManager<Usuario> signInManager,
    IHttpContextAccessor httpContextAccessor,
    IPermissoesDoUsuario permissoes) : ITrocaDeSenha
{
    private HttpContext Contexto => httpContextAccessor.HttpContext
        ?? throw new InvalidOperationException("Troca de senha usada fora de uma requisição HTTP.");

    public Guid? UsuarioDaSessao() =>
        Contexto.User.Identity?.IsAuthenticated == true && Guid.TryParse(userManager.GetUserId(Contexto.User), out var id)
            ? id
            : null;

    public async Task<IReadOnlyList<string>> TrocarAsync(Guid usuarioId, string novaSenha, CancellationToken ct)
    {
        var usuario = await userManager.FindByIdAsync(usuarioId.ToString())
            ?? throw new InvalidOperationException($"Usuário {usuarioId} não encontrado ao trocar a senha.");

        var erros = await GestaoUsuarios.ValidarSenhaAsync(userManager, usuario, novaSenha);
        if (erros.Count > 0)
            return erros;

        usuario.PasswordHash = userManager.PasswordHasher.HashPassword(usuario, novaSenha);
        usuario.ConcluirTrocaDeSenha();
        var gravado = await userManager.UpdateSecurityStampAsync(usuario);
        if (!gravado.Succeeded)
            throw new InvalidOperationException(
                $"Não foi possível trocar a senha de {usuarioId}: {string.Join("; ", gravado.Errors.Select(e => e.Description))}");
        permissoes.Invalidar(); // a marca sai já na próxima requisição

        // O stamp mudou: outras sessões (com a senha anterior) caem na validação do cookie; esta é renovada com o
        // stamp novo — o handler do cookie revoga a sessão antiga no banco e grava a nova, mantendo o fim das 12 h.
        await signInManager.RefreshSignInAsync(usuario);
        Contexto.User = await signInManager.CreateUserPrincipalAsync(usuario);
        return [];
    }
}
