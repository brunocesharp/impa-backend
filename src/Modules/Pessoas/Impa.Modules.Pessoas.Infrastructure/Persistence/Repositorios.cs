using Impa.Modules.Pessoas.Application.Clientes;
using Impa.Modules.Pessoas.Application.Comum;
using Impa.Modules.Pessoas.Application.Fornecedores;
using Impa.Modules.Pessoas.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Impa.Modules.Pessoas.Infrastructure.Persistence;

internal static class Busca
{
    public const string Escape = "\\";

    /// <summary>"%palavra%" com os curingas do LIKE escapados.</summary>
    public static string Contendo(string palavra) =>
        "%" + palavra.Replace(Escape, Escape + Escape).Replace("%", Escape + "%").Replace("_", Escape + "_") + "%";

    /// <summary>
    /// Índice único do documento recusou a gravação (23505). Desfaz o rastreamento para o contexto seguir utilizável
    /// e não deixa a exceção (com o documento no texto) chegar a um log.
    /// </summary>
    public static async Task<bool> SalvarAsync(DbContext db, string indice, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
                                           { SqlState: PostgresErrorCodes.UniqueViolation } pg
                                           && pg.ConstraintName == indice)
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }
}

internal sealed class ClientesRepository(PessoasDbContext db) : IClientesRepository
{
    public Task<Cliente?> ObterAsync(Guid id, CancellationToken ct) =>
        db.Clientes.FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<Cliente?> ObterPorDocumentoAsync(string documento, Guid? exceto, CancellationToken ct) =>
        db.Clientes.AsNoTracking()
            .Where(c => c.Documento == documento && (exceto == null || c.Id != exceto))
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<Cliente>> BuscarAsync(TermosDeBusca termos, CancellationToken ct)
    {
        var consulta = db.Clientes.AsNoTracking();
        if (!termos.IncluirInativos)
            consulta = consulta.Where(c => c.Ativo);

        if (termos.Digitos is { } digitos)
        {
            // RN-14.3: CPF/CNPJ (começo do número) ou telefone (qualquer parte), só dígitos.
            var telefone = Busca.Contendo(digitos);
            consulta = consulta.Where(c => (c.Documento != null && c.Documento.StartsWith(digitos))
                                           || (c.Telefone != null && EF.Functions.Like(c.Telefone, telefone, Busca.Escape)));
        }

        // RN-3.11: cada palavra em alguma parte do nome, sem diferenciar maiúsculas nem acentos, em qualquer ordem.
        foreach (var palavra in termos.Palavras)
        {
            var padrao = Busca.Contendo(palavra);
            consulta = consulta.Where(c =>
                EF.Functions.ILike(EF.Functions.Unaccent(c.Nome), EF.Functions.Unaccent(padrao), Busca.Escape));
        }

        return await consulta.OrderBy(c => c.Nome).ThenBy(c => c.Id).Take(termos.Limite).ToListAsync(ct);
    }

    public async Task<bool> AdicionarAsync(Cliente cliente, CancellationToken ct)
    {
        db.Clientes.Add(cliente);
        return await Busca.SalvarAsync(db, IndicesUnicos.DocumentoDoCliente, ct);
    }

    public Task<bool> SalvarAsync(CancellationToken ct) => Busca.SalvarAsync(db, IndicesUnicos.DocumentoDoCliente, ct);

    public async Task RemoverAsync(Cliente cliente, CancellationToken ct)
    {
        db.Clientes.Remove(cliente);
        await db.SaveChangesAsync(ct);
    }
}

internal sealed class FornecedoresRepository(PessoasDbContext db) : IFornecedoresRepository
{
    public Task<Fornecedor?> ObterAsync(Guid id, CancellationToken ct) =>
        db.Fornecedores.FirstOrDefaultAsync(f => f.Id == id, ct);

    public Task<Fornecedor?> ObterPorDocumentoAsync(string documento, Guid? exceto, CancellationToken ct) =>
        db.Fornecedores.AsNoTracking()
            .Where(f => f.Documento == documento && (exceto == null || f.Id != exceto))
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<Fornecedor>> BuscarAsync(TermosDeBusca termos, CancellationToken ct)
    {
        var consulta = db.Fornecedores.AsNoTracking();
        if (!termos.IncluirInativos)
            consulta = consulta.Where(f => f.Ativo);

        // RN-14.3: CNPJ (começo do número, só dígitos).
        if (termos.Digitos is { } digitos)
            consulta = consulta.Where(f => f.Documento.StartsWith(digitos));

        // RN-14.3: razão social ou nome fantasia, com as regras da RN-3.11.
        foreach (var palavra in termos.Palavras)
        {
            var padrao = Busca.Contendo(palavra);
            consulta = consulta.Where(f =>
                EF.Functions.ILike(EF.Functions.Unaccent(f.RazaoSocial), EF.Functions.Unaccent(padrao), Busca.Escape)
                || (f.NomeFantasia != null
                    && EF.Functions.ILike(EF.Functions.Unaccent(f.NomeFantasia), EF.Functions.Unaccent(padrao), Busca.Escape)));
        }

        return await consulta.OrderBy(f => f.RazaoSocial).ThenBy(f => f.Id).Take(termos.Limite).ToListAsync(ct);
    }

    public async Task<bool> AdicionarAsync(Fornecedor fornecedor, CancellationToken ct)
    {
        db.Fornecedores.Add(fornecedor);
        return await Busca.SalvarAsync(db, IndicesUnicos.DocumentoDoFornecedor, ct);
    }

    public Task<bool> SalvarAsync(CancellationToken ct) => Busca.SalvarAsync(db, IndicesUnicos.DocumentoDoFornecedor, ct);

    public async Task RemoverAsync(Fornecedor fornecedor, CancellationToken ct)
    {
        db.Fornecedores.Remove(fornecedor);
        await db.SaveChangesAsync(ct);
    }
}
