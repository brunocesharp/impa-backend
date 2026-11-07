using Impa.Modules.Acesso.Application.Usuarios;
using Impa.Modules.Acesso.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Impa.Modules.Acesso.Infrastructure.Identity;

public sealed class GestaoUsuarios(UserManager<Usuario> userManager, AcessoDbContext banco, TimeProvider relogio)
    : IGestaoUsuarios
{
    public Task<bool> ExisteAsync(Guid usuarioId, CancellationToken ct) =>
        banco.Users.AnyAsync(u => u.Id == usuarioId, ct);

    public async Task DesativarAsync(Guid usuarioId, CancellationToken ct)
    {
        // O UserManager usa este mesmo AcessoDbContext (escopo da requisição): as três gravações vão juntas.
        await using var transacao = await banco.Database.BeginTransactionAsync(ct);

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

        await transacao.CommitAsync(ct);
    }
}
