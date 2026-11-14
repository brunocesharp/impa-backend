using Impa.Modules.Acesso.Application.Liberacoes;
using Impa.Modules.Acesso.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Impa.Modules.Acesso.Infrastructure.Liberacoes;

/// <summary>Liberações no schema <c>acesso</c> (story 1-5). As regras ficam no handler da Application.</summary>
public sealed class GestaoLiberacoes(AcessoDbContext banco, TimeProvider relogio) : ILiberacoes
{
    public async Task RegistrarAsync(NovaLiberacao liberacao, CancellationToken ct)
    {
        banco.Liberacoes.Add(new Liberacao(
            Guid.CreateVersion7(),
            liberacao.TokenHash,
            liberacao.SessaoId,
            liberacao.OperadorId,
            liberacao.LiberadorId,
            liberacao.Permissao,
            relogio.GetUtcNow(),
            liberacao.ExpiraEm));
        await banco.SaveChangesAsync(ct);
    }

    public async Task<Guid?> ObterLiberadorAsync(byte[] tokenHash, Guid sessaoId, string permissao, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        return await banco.Liberacoes.AsNoTracking()
            .Where(l => l.TokenHash == tokenHash && l.SessaoId == sessaoId && l.Permissao == permissao
                && l.UsadaEm == null && l.ExpiraEm > agora)
            .Select(l => (Guid?)l.LiberadorId)
            .SingleOrDefaultAsync(ct);
    }

    // Uso único: o UPDATE marca o uso e o WHERE é reavaliado sobre a linha travada — de duas requisições com o mesmo
    // token, a segunda já vê "UsadaEm" preenchido e não recebe linha.
    public async Task<Guid?> ConsumirAsync(byte[] tokenHash, Guid sessaoId, string permissao, string operacao, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        var operacaoGravada = operacao.Length > Liberacao.OperacaoMaximo ? operacao[..Liberacao.OperacaoMaximo] : operacao;

        var liberadores = await banco.Database.SqlQuery<Guid>($"""
            UPDATE acesso.liberacoes SET "UsadaEm" = {agora}, "Operacao" = {operacaoGravada}
            WHERE "TokenHash" = {tokenHash}
              AND "SessaoId" = {sessaoId}
              AND "Permissao" = {permissao}
              AND "UsadaEm" IS NULL
              AND "ExpiraEm" > {agora}
            RETURNING "LiberadorId" AS "Value"
            """).ToListAsync(ct);

        return liberadores.Count == 0 ? null : liberadores[0];
    }
}
