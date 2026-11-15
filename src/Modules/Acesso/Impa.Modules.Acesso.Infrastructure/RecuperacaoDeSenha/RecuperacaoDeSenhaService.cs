using Impa.Modules.Acesso.Application.Autenticacao;
using Impa.Modules.Acesso.Application.RecuperacaoDeSenha;
using Impa.Modules.Acesso.Infrastructure.Identity;
using Impa.Modules.Acesso.Infrastructure.Persistence;
using Impa.Shared.Messaging;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
    IOptions<OpcoesDaEmpresa> empresa,
    TimeProvider relogio,
    ILogger<RecuperacaoDeSenhaService> log) : IRecuperacaoDeSenha
{
    private static readonly string TipoDoPedido = EventoDeIntegracaoAttribute.De(typeof(RecuperacaoDeSenhaSolicitada)).Tipo;

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

        // D2-a (revisão 1): com outro pedido pendente ou entregue há menos de N minutos, o novo é ignorado — mesma
        // resposta, nenhum e-mail. Limita o envio por conta de destino (o rate limit é só por IP).
        var agora = relogio.GetUtcNow();
        var desde = agora - opcoes.Value.IntervaloMinimoEntrePedidos;
        var chaves = destinatarios.Select(Chave).ToList();
        var recentes = await banco.Set<OutboxMensagem>().AsNoTracking()
            .Where(m => m.Tipo == TipoDoPedido && chaves.Contains(m.ChaveDeCorrelacao!) && m.OcorreuEm > desde
                        && (m.Situacao == SituacaoDoEvento.Pendente || m.Situacao == SituacaoDoEvento.Entregue))
            .Select(m => m.ChaveDeCorrelacao)
            .ToListAsync(ct);

        var publicados = 0;
        foreach (var id in destinatarios.Where(id => !recentes.Contains(Chave(id))))
        {
            eventos.Publicar(new RecuperacaoDeSenhaSolicitada(id) { OcorreuEm = agora });
            publicados++;
        }

        if (publicados > 0)
            await banco.SaveChangesAsync(ct);
    }

    public async Task<EnvioDoLink?> PrepararEnvioAsync(Guid usuarioId, DateTimeOffset pedidoEm, CancellationToken ct)
    {
        var o = opcoes.Value;
        if (relogio.GetUtcNow() - pedidoEm > o.IdadeMaximaDoPedido)
        {
            log.LogInformation("Pedido de recuperação de senha de {UsuarioId} descartado: feito em {PedidoEm:O}, passou de {Minutos} minutos",
                usuarioId, pedidoEm, o.IdadeMaximaDoPedidoEmMinutos);
            return null;
        }

        var usuario = await userManager.FindByIdAsync(usuarioId.ToString());
        if (usuario is not { Ativo: true } || string.IsNullOrWhiteSpace(usuario.Email))
            return null;

        var stamp = await userManager.GetSecurityStampAsync(usuario);
        var token = tokens.Gerar(usuario.Id, stamp, userManager.NormalizeEmail(usuario.Email), IRecuperacaoDeSenha.Validade);
        return new EnvioDoLink(
            usuario.Email, usuario.NomeExibicao, usuario.UserName ?? string.Empty,
            $"{o.Base}/redefinir-senha#token={Uri.EscapeDataString(token)}",
            IRecuperacaoDeSenha.Validade,
            empresa.Value.Nome is { Length: > 0 } nome ? nome : "IMPA",
            $"{o.Base}/brand/logo-horizontal.png"); // asset da SPA (contrato registrado na story 1-8)
    }

    public async Task<ResultadoDaRedefinicao> RedefinirAsync(string token, string novaSenha, CancellationToken ct)
    {
        if (tokens.Ler(token) is not { } lido)
            return ResultadoDaRedefinicao.Invalido;

        var usuario = await userManager.FindByIdAsync(lido.UsuarioId.ToString());
        if (!LinkValePara(usuario, lido))
            return ResultadoDaRedefinicao.Invalido;

        // Senha recusada (RN-1.1) não consome o link: dá para tentar de novo com outra.
        var erros = await GestaoUsuarios.ValidarSenhaAsync(userManager, usuario!, novaSenha);
        if (erros.Count > 0)
            return ResultadoDaRedefinicao.SenhaRecusada(erros);

        await using var transacao = await banco.Database.BeginTransactionAsync(ct);

        // O ConcurrencyStamp do Identity barra o mesmo link usado duas vezes ao mesmo tempo. Mas ele também muda com
        // gravações que não invalidam o link (senha errada no login, R7 da revisão 1): no conflito, relê o usuário e só
        // recusa se o link deixou de valer (stamp trocado, desativado, e-mail trocado); senão, tenta de novo uma vez.
        for (var tentativa = 1; ; tentativa++)
        {
            usuario!.PasswordHash = userManager.PasswordHasher.HashPassword(usuario, novaSenha);
            usuario.ConcluirTrocaDeSenha(); // a senha agora é dele: a provisória (RN-1.8), se houver, deixa de ser exigida
            usuario.AccessFailedCount = 0;  // RN-1.11: a contagem de bloqueio é zerada
            usuario.LockoutEnd = null;

            var gravado = await userManager.UpdateSecurityStampAsync(usuario);
            if (gravado.Succeeded)
                break;
            if (!gravado.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure)))
                throw new InvalidOperationException(
                    $"Não foi possível redefinir a senha de {lido.UsuarioId}: {string.Join("; ", gravado.Errors.Select(e => e.Description))}");

            await banco.Entry(usuario).ReloadAsync(ct);
            if (tentativa == 2 || !LinkValePara(usuario, lido))
                return ResultadoDaRedefinicao.Invalido;
        }

        // RN-1.11: as sessões abertas são encerradas (o stamp novo também derrubaria o cookie na validação).
        var agora = relogio.GetUtcNow();
        await banco.Sessoes
            .Where(s => s.UsuarioId == lido.UsuarioId && s.RevogadaEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevogadaEm, agora), ct);

        await transacao.CommitAsync(ct);
        permissoes.Invalidar();
        return ResultadoDaRedefinicao.Sucesso(lido.UsuarioId);
    }

    /// <summary>
    /// Ativo, com o mesmo stamp do token (já usado ou senha trocada de outro jeito → não) e com o e-mail para o qual o
    /// link foi enviado (trocado ou apagado pelo administrador depois do envio → não, R10 da revisão 1).
    /// </summary>
    private bool LinkValePara(Usuario? usuario, TokenLido lido) =>
        usuario is { Ativo: true }
        && usuario.SecurityStamp == lido.SecurityStamp
        && !string.IsNullOrWhiteSpace(usuario.Email)
        && lido.FoiEnviadoPara(userManager.NormalizeEmail(usuario.Email));

    /// <summary>Igual à <see cref="RecuperacaoDeSenhaSolicitada.ChaveDeCorrelacao"/>.</summary>
    private static string Chave(Guid usuarioId) => $"usuario:{usuarioId}";
}
