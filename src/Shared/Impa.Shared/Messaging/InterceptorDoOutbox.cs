using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Impa.Shared.Messaging;

/// <summary>
/// Acorda o despachante quando um <c>SaveChanges</c> gravou eventos no Outbox: na hora, se não havia
/// transação explícita; senão, só depois do commit (rollback descarta o aviso). Uma instância serve
/// todos os contextos (registrada como singleton por <c>UsarOutbox</c>).
/// </summary>
public sealed class InterceptorDoOutbox(SinalDoDespachante sinal) : SaveChangesInterceptor, IDbTransactionInterceptor
{
    private sealed class Marca
    {
        public bool GravandoEventos;
        public bool EventosNaTransacao;
    }

    private readonly ConditionalWeakTable<DbContext, Marca> _marcas = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Marcar(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Marcar(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Gravou(eventData.Context);
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Gravou(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Limpar(eventData.Context, tudo: false);

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Limpar(eventData.Context, tudo: false);
        return Task.CompletedTask;
    }

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => Commit(eventData.Context);

    public Task TransactionCommittedAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Commit(eventData.Context);
        return Task.CompletedTask;
    }

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) =>
        Limpar(eventData.Context, tudo: true);

    public Task TransactionRolledBackAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Limpar(eventData.Context, tudo: true);
        return Task.CompletedTask;
    }

    private void Marcar(DbContext? contexto)
    {
        if (contexto is null)
            return;
        var gravando = contexto.ChangeTracker.Entries<OutboxMensagem>().Any(e => e.State == EntityState.Added);
        if (gravando)
            _marcas.GetOrCreateValue(contexto).GravandoEventos = true;
    }

    private void Gravou(DbContext? contexto)
    {
        if (contexto is null || !_marcas.TryGetValue(contexto, out var marca) || !marca.GravandoEventos)
            return;

        marca.GravandoEventos = false;
        if (contexto.Database.CurrentTransaction is null)
            sinal.Acordar();
        else
            marca.EventosNaTransacao = true;
    }

    private void Commit(DbContext? contexto)
    {
        if (contexto is null || !_marcas.TryGetValue(contexto, out var marca) || !marca.EventosNaTransacao)
            return;
        marca.EventosNaTransacao = false;
        sinal.Acordar();
    }

    private void Limpar(DbContext? contexto, bool tudo)
    {
        if (contexto is null || !_marcas.TryGetValue(contexto, out var marca))
            return;
        marca.GravandoEventos = false;
        if (tudo)
            marca.EventosNaTransacao = false;
    }
}
