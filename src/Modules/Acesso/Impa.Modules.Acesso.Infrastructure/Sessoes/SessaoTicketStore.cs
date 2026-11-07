using System.Security.Claims;
using Impa.Modules.Acesso.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Impa.Modules.Acesso.Infrastructure.Sessoes;

/// <summary>
/// Guarda o tíquete do cookie de sessão na tabela <c>acesso.sessoes</c> (ADR-006): o cookie leva só a chave.
/// Logout e desativação revogam a sessão no servidor, e uma cópia do cookie deixa de valer na hora
/// (antes, o tíquete era autocontido e valia até as 12 h — story 1-1, revisão 1, A19).
/// Singleton (fica nas opções do cookie); o banco é o da requisição.
/// </summary>
public sealed class SessaoTicketStore(TimeProvider relogio) : ITicketStore
{
    /// <summary>Usado só se o cookie não informar o fim (ele sempre informa: ExpireTimeSpan de 12 h).</summary>
    public static readonly TimeSpan DuracaoPadrao = TimeSpan.FromHours(12);

    public async Task<string> StoreAsync(AuthenticationTicket ticket, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var agora = relogio.GetUtcNow();
        var sessao = new Sessao(
            Guid.NewGuid(),
            UsuarioDo(ticket),
            agora,
            ticket.Properties.ExpiresUtc ?? agora.Add(DuracaoPadrao),
            TicketSerializer.Default.Serialize(ticket));

        var banco = BancoDa(httpContext);
        banco.Sessoes.Add(sessao);
        await banco.SaveChangesAsync(cancellationToken);
        return Chave(sessao.Id);
    }

    public async Task RenewAsync(string key, AuthenticationTicket ticket, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (!TryId(key, out var id))
            return;

        var banco = BancoDa(httpContext);
        var sessao = await banco.Sessoes.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (sessao is null)
            return;

        // Renovação pelo SecurityStampValidator: o fim continua o do login (SlidingExpiration = false).
        sessao.Renovar(TicketSerializer.Default.Serialize(ticket), ticket.Properties.ExpiresUtc);
        await banco.SaveChangesAsync(cancellationToken);
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (!TryId(key, out var id))
            return null;

        var sessao = await BancoDa(httpContext).Sessoes.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == id, cancellationToken);

        // Revogada (logout, usuário desativado) ou vencida: o cookie não autentica, e a API responde 401.
        return sessao is not null && sessao.ValeEm(relogio.GetUtcNow())
            ? TicketSerializer.Default.Deserialize(sessao.Tiquete)
            : null;
    }

    public async Task RemoveAsync(string key, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (!TryId(key, out var id))
            return;

        var banco = BancoDa(httpContext);
        var sessao = await banco.Sessoes.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (sessao is null)
            return;

        // Logout: a linha fica, revogada, como registro da sessão.
        sessao.Revogar(relogio.GetUtcNow());
        await banco.SaveChangesAsync(cancellationToken);
    }

    // O handler do cookie sempre chama as sobrecargas com HttpContext; sem ela não há banco da requisição.
    public Task<string> StoreAsync(AuthenticationTicket ticket) => throw SemRequisicao();

    public Task RenewAsync(string key, AuthenticationTicket ticket) => throw SemRequisicao();

    public Task<AuthenticationTicket?> RetrieveAsync(string key) => throw SemRequisicao();

    public Task RemoveAsync(string key) => throw SemRequisicao();

    private static AcessoDbContext BancoDa(HttpContext httpContext) =>
        httpContext.RequestServices.GetRequiredService<AcessoDbContext>();

    private static Guid UsuarioDo(AuthenticationTicket ticket) =>
        Guid.TryParse(ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw new InvalidOperationException("Tíquete de sessão sem o id do usuário.");

    private static string Chave(Guid id) => id.ToString("N");

    private static bool TryId(string key, out Guid id) => Guid.TryParseExact(key, "N", out id);

    private static NotSupportedException SemRequisicao() =>
        new("O SessaoTicketStore precisa do HttpContext da requisição.");
}
