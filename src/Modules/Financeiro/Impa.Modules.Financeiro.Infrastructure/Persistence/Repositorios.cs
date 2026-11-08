using Impa.Modules.Financeiro.Application.Categorias;
using Impa.Modules.Financeiro.Application.Contas;
using Impa.Modules.Financeiro.Application.FormasDePagamento;
using Impa.Modules.Financeiro.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Impa.Modules.Financeiro.Infrastructure.Persistence;

internal sealed class CategoriasRepository(FinanceiroDbContext db) : ICategoriasRepository
{
    public Task<CategoriaFinanceira?> ObterAsync(Guid id, CancellationToken ct) =>
        db.Categorias.FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<CategoriaFinanceira?> ObterPorNomeAsync(string nome, Guid? exceto, CancellationToken ct)
    {
        var minusculo = nome.ToLower();
        return db.Categorias.AsNoTracking()
            .Where(c => c.Nome.ToLower() == minusculo && (exceto == null || c.Id != exceto))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<CategoriaFinanceira>> ListarAsync(bool incluirInativas, CancellationToken ct)
    {
        var consulta = db.Categorias.AsNoTracking();
        if (!incluirInativas)
            consulta = consulta.Where(c => c.Ativa);
        return await consulta.OrderBy(c => c.Nome).ThenBy(c => c.Id).ToListAsync(ct);
    }

    public async Task<bool> AdicionarAsync(CategoriaFinanceira categoria, CancellationToken ct)
    {
        db.Categorias.Add(categoria);
        return await SalvarAsync(ct);
    }

    /// <summary>O índice único do nome recusou (23505): desfaz o rastreamento para o contexto seguir utilizável.</summary>
    public async Task<bool> SalvarAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
                                           { SqlState: PostgresErrorCodes.UniqueViolation } pg
                                           && pg.ConstraintName == IndicesUnicos.NomeDaCategoria)
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    /// <summary>A FK das contas (ON DELETE RESTRICT) recusou (23503): desfaz o rastreamento e devolve falso.</summary>
    public async Task<bool> RemoverAsync(CategoriaFinanceira categoria, CancellationToken ct)
    {
        db.Categorias.Remove(categoria);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }
}

internal sealed class ContasRepository(FinanceiroDbContext db) : IContasRepository
{
    public Task<Conta?> ObterAsync(Guid id, CancellationToken ct) =>
        db.Contas.Include(c => c.Baixas).FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<Conta?> ObterPelaBaixaAsync(Guid baixaId, CancellationToken ct) =>
        db.Contas.Include(c => c.Baixas).FirstOrDefaultAsync(c => c.Baixas.Any(b => b.Id == baixaId), ct);

    public async Task<IReadOnlyList<Conta>> ListarAsync(CancellationToken ct) =>
        await db.Contas.AsNoTracking().Include(c => c.Baixas).AsSplitQuery()
            .OrderBy(c => c.Vencimento).ThenBy(c => c.Descricao).ThenBy(c => c.Parcela).ThenBy(c => c.Id)
            .ToListAsync(ct);

    public async Task AdicionarAsync(IReadOnlyList<Conta> contas, CancellationToken ct)
    {
        db.Contas.AddRange(contas);
        await db.SaveChangesAsync(ct); // um SaveChanges = uma transação: todas as parcelas ou nenhuma
    }

    /// <summary>
    /// A versão da linha (xmin) mudou desde a leitura, ou a FK da forma de pagamento recusou a baixa nova (23503):
    /// desfaz o rastreamento para o contexto seguir utilizável.
    /// </summary>
    public async Task<GravacaoDaConta> SalvarAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return GravacaoDaConta.Gravada;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return GravacaoDaConta.AlteradaAoMesmoTempo;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
                                           { SqlState: PostgresErrorCodes.ForeignKeyViolation } pg
                                           && pg.ConstraintName == ChavesEstrangeiras.FormaDePagamentoDaBaixa)
        {
            db.ChangeTracker.Clear();
            return GravacaoDaConta.FormaDePagamentoNaoEncontrada;
        }
    }

    public Task<bool> ExisteBaixaDaFormaAsync(Guid formaDePagamentoId, CancellationToken ct) =>
        db.Baixas.AnyAsync(b => b.FormaDePagamentoId == formaDePagamentoId, ct);

    /// <summary>A versão da linha (xmin) também entra no DELETE: mudou desde a leitura → desfaz o rastreamento e devolve falso.</summary>
    public async Task<bool> RemoverAsync(Conta conta, CancellationToken ct)
    {
        db.Contas.Remove(conta);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    public Task<bool> ExisteDaCategoriaAsync(Guid categoriaId, CancellationToken ct) =>
        db.Contas.AnyAsync(c => c.CategoriaId == categoriaId, ct);

    public Task<bool> ExisteDoFornecedorAsync(Guid fornecedorId, CancellationToken ct) =>
        db.Contas.AnyAsync(c => c.FornecedorId == fornecedorId, ct);
}

internal sealed class FormasDePagamentoRepository(FinanceiroDbContext db) : IFormasDePagamentoRepository
{
    public Task<FormaDePagamentoFinanceira?> ObterAsync(Guid id, CancellationToken ct) =>
        db.FormasDePagamento.FirstOrDefaultAsync(f => f.Id == id, ct);

    public Task<FormaDePagamentoFinanceira?> ObterPorNomeAsync(string nome, Guid? exceto, CancellationToken ct)
    {
        var minusculo = nome.ToLower();
        return db.FormasDePagamento.AsNoTracking()
            .Where(f => f.Nome.ToLower() == minusculo && (exceto == null || f.Id != exceto))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<FormaDePagamentoFinanceira>> ListarAsync(bool incluirInativas, CancellationToken ct)
    {
        var consulta = db.FormasDePagamento.AsNoTracking();
        if (!incluirInativas)
            consulta = consulta.Where(f => f.Ativa);
        return await consulta.OrderBy(f => f.Nome).ThenBy(f => f.Id).ToListAsync(ct);
    }

    public async Task<bool> AdicionarAsync(FormaDePagamentoFinanceira forma, CancellationToken ct)
    {
        db.FormasDePagamento.Add(forma);
        return await SalvarAsync(ct);
    }

    /// <summary>O índice único do nome recusou (23505): desfaz o rastreamento para o contexto seguir utilizável.</summary>
    public async Task<bool> SalvarAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
                                           { SqlState: PostgresErrorCodes.UniqueViolation } pg
                                           && pg.ConstraintName == IndicesUnicos.NomeDaFormaDePagamento)
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    /// <summary>A FK das baixas (ON DELETE RESTRICT) recusou (23503): desfaz o rastreamento e devolve falso.</summary>
    public async Task<bool> RemoverAsync(FormaDePagamentoFinanceira forma, CancellationToken ct)
    {
        db.FormasDePagamento.Remove(forma);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }
}
