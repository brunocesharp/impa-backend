using Impa.Modules.Acesso.Application.Terminais;
using Impa.Modules.Acesso.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Impa.Modules.Acesso.Infrastructure.Terminais;

/// <summary>Terminais no schema <c>acesso</c> (story 1-6). As regras ficam nos handlers da Application.</summary>
public sealed class GestaoTerminais(AcessoDbContext banco, TimeProvider relogio) : ITerminais
{
    public async Task<IReadOnlyList<TerminalDto>> ListarAsync(byte[]? hashAtual, CancellationToken ct)
    {
        var linhas = await Consulta(banco.Terminais).ToListAsync(ct);

        return linhas
            .OrderBy(l => l.Terminal.RevogadoEm is not null).ThenBy(l => l.Terminal.Nome, StringComparer.CurrentCultureIgnoreCase)
            .Select(l => ParaDto(l, hashAtual))
            .ToList();
    }

    public async Task<TerminalDto?> ObterAsync(Guid id, CancellationToken ct) =>
        await Consulta(banco.Terminais.Where(t => t.Id == id)).SingleOrDefaultAsync(ct) is { } linha ? ParaDto(linha, null) : null;

    public Task<TerminalAtualDto?> ObterValidoAsync(byte[] tokenHash, CancellationToken ct) =>
        banco.Terminais.AsNoTracking()
            .Where(t => t.TokenHash == tokenHash && t.RevogadoEm == null)
            .Select(t => new TerminalAtualDto(t.Id, t.Nome))
            .SingleOrDefaultAsync(ct);

    public Task<bool> NomeEmUsoAsync(string nome, Guid? exceto, CancellationToken ct)
    {
        var normalizado = Terminal.Normalizar(nome);
        return banco.Terminais.AnyAsync(t => t.NomeNormalizado == normalizado && t.RevogadoEm == null && t.Id != exceto, ct);
    }

    public async Task<TerminalDto> RegistrarAsync(string nome, Guid criadoPor, byte[] tokenHash, CancellationToken ct)
    {
        var terminal = new Terminal(Guid.CreateVersion7(), nome, tokenHash, criadoPor, relogio.GetUtcNow());
        banco.Terminais.Add(terminal);
        await SalvarAsync(ct);
        return ParaDto(await Consulta(banco.Terminais.Where(t => t.Id == terminal.Id)).SingleAsync(ct), tokenHash);
    }

    public async Task<TerminalDto> RenomearAsync(Guid id, string nome, CancellationToken ct)
    {
        var terminal = await banco.Terminais.SingleAsync(t => t.Id == id, ct);
        terminal.Renomear(nome);
        await SalvarAsync(ct);
        return (await ObterAsync(id, ct))!;
    }

    public async Task RevogarAsync(Guid id, CancellationToken ct)
    {
        var terminal = await banco.Terminais.SingleAsync(t => t.Id == id, ct);
        terminal.Revogar(relogio.GetUtcNow());
        await banco.SaveChangesAsync(ct);
    }

    private sealed class Linha
    {
        public required Terminal Terminal { get; init; }

        public string? CriadoPor { get; init; }
    }

    /// <summary>Terminais (já filtrados) com o nome de quem registrou.</summary>
    private IQueryable<Linha> Consulta(IQueryable<Terminal> terminais) =>
        from t in terminais.AsNoTracking()
        join u in banco.Users.AsNoTracking() on t.CriadoPor equals u.Id into criadores
        from u in criadores.DefaultIfEmpty()
        select new Linha { Terminal = t, CriadoPor = u == null ? null : u.NomeExibicao };

    private static TerminalDto ParaDto(Linha linha, byte[]? hashAtual) => new(
        linha.Terminal.Id,
        linha.Terminal.Nome,
        linha.CriadoPor ?? string.Empty,
        linha.Terminal.CriadoEm,
        linha.Terminal.RevogadoEm,
        hashAtual is not null && linha.Terminal.TokenHash.AsSpan().SequenceEqual(hashAtual));

    /// <summary>
    /// O handler já confere o nome; o índice único parcial fecha a corrida entre dois registros simultâneos
    /// e vira o mesmo conflito.
    /// </summary>
    private async Task SalvarAsync(CancellationToken ct)
    {
        try
        {
            await banco.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new NomeDeTerminalEmUsoException(e);
        }
    }
}
