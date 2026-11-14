using Impa.Modules.Acesso.Application.Autenticacao;
using Impa.Modules.Acesso.Application.RecuperacaoDeSenha;
using Impa.Modules.Acesso.Infrastructure.Identity;
using Impa.Modules.Acesso.Infrastructure.Persistence;
using Impa.Shared.Messaging;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Impa.Modules.Acesso.Infrastructure.RecuperacaoDeSenha;

/// <summary>Recuperação de senha por e-mail (story 1-8, RN-1.11) com o Identity, o Outbox do Acesso e as sessões no banco.</summary>
public sealed class RecuperacaoDeSenhaService(
    UserManager<Usuario> userManager,
    AcessoDbContext banco,
    IEventPublisher<AcessoDbContext> eventos,
    TokenDeRedefinicao tokens,
    IPermissoesDoUsuario permissoes,
    IOptions<OpcoesDaRecuperacaoDeSenha> opcoes,
    IConfiguration configuracao,
    TimeProvider relogio) : IRecuperacaoDeSenha
{
    public async Task SolicitarAsync(string usuarioOuEmail, CancellationToken ct)
    {
        var login = userManager.NormalizeName(usuarioOuEmail);
        var email = userManager.NormalizeEmail(usuarioOuEmail);

        var destinatarios = await banco.Users.AsNoTracking()
            .Where(u => u.Ativo && u.Email != null && u.Email != ""
                        && (u.NormalizedUserName == login || u.NormalizedEmail == email))
            .Select(u => u.Id)
            .ToListAsync(ct);
        if (destinatarios.Count == 0)
            return;

        foreach (var id in destinatarios)
            eventos.Publicar(new RecuperacaoDeSenhaSolicitada(id));
        await banco.SaveChangesAsync(ct);
    }

    public async Task<EnvioDoLink?> PrepararEnvioAsync(Guid usuarioId, CancellationToken ct)
    {
        var usuario = await userManager.FindByIdAsync(usuarioId.ToString());
        if (usuario is not { Ativo: true } || string.IsNullOrWhiteSpace(usuario.Email))
            return null;

        var stamp = await userManager.GetSecurityStampAsync(usuario);
        var token = tokens.Gerar(usuario.Id, stamp, IRecuperacaoDeSenha.Validade);
        var url = opcoes.Value.Base;
        return new EnvioDoLink(
            usuario.Email, usuario.NomeExibicao, usuario.UserName ?? string.Empty,
            $"{url}/redefinir-senha#token={Uri.EscapeDataString(token)}",
            IRecuperacaoDeSenha.Validade,
            configuracao["Empresa:Nome"] is { Length: > 0 } empresa ? empresa : "IMPA",
            $"{url}/brand/logo-horizontal.png");
    }

    public async Task<ResultadoDaRedefinicao> RedefinirAsync(string token, string novaSenha, CancellationToken ct)
    {
        if (tokens.Ler(token) is not { } lido)
            return ResultadoDaRedefinicao.Invalido;
        var (usuarioId, stampDoToken) = lido;

        var usuario = await userManager.FindByIdAsync(usuarioId.ToString());
        if (usuario is not { Ativo: true } || usuario.SecurityStamp != stampDoToken)
            return ResultadoDaRedefinicao.Invalido; // já usado (stamp trocado), senha trocada de outro jeito ou desativado

        // Senha recusada (RN-1.1) não consome o link: dá para tentar de novo com outra.
        var erros = await GestaoUsuarios.ValidarSenhaAsync(userManager, usuario, novaSenha);
        if (erros.Count > 0)
            return ResultadoDaRedefinicao.SenhaRecusada(erros);

        await using var transacao = await banco.Database.BeginTransactionAsync(ct);

        usuario.PasswordHash = userManager.PasswordHasher.HashPassword(usuario, novaSenha);
        usuario.ConcluirTrocaDeSenha(); // a senha agora é dele: a provisória (RN-1.8), se houver, deixa de ser exigida
        usuario.AccessFailedCount = 0;  // RN-1.11: a contagem de bloqueio é zerada
        usuario.LockoutEnd = null;

        // Troca o stamp e grava tudo; o ConcurrencyStamp do Identity barra o mesmo link usado duas vezes ao mesmo tempo.
        var gravado = await userManager.UpdateSecurityStampAsync(usuario);
        if (gravado.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure)))
            return ResultadoDaRedefinicao.Invalido;
        if (!gravado.Succeeded)
            throw new InvalidOperationException(
                $"Não foi possível redefinir a senha de {usuarioId}: {string.Join("; ", gravado.Errors.Select(e => e.Description))}");

        // RN-1.11: as sessões abertas são encerradas (o stamp novo também derrubaria o cookie na validação).
        var agora = relogio.GetUtcNow();
        await banco.Sessoes
            .Where(s => s.UsuarioId == usuarioId && s.RevogadaEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevogadaEm, agora), ct);

        await transacao.CommitAsync(ct);
        permissoes.Invalidar();
        return ResultadoDaRedefinicao.Sucesso;
    }
}
