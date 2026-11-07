using System.Collections.Concurrent;
using Impa.Shared.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Impa.Messaging.Tests.Modulos;

// Módulos de teste: "Pedidos" publica; "Estoque" e "Fiscal" consomem, cada um no próprio schema (ADR-012).

[EventoDeIntegracao("pedidos.pedido-registrado", 1)]
public sealed record PedidoRegistrado(Guid PedidoId, string Item) : IntegrationEvent
{
    public override string? ChaveDeCorrelacao => $"item:{Item}";
}

public sealed class Pedido
{
    public Guid Id { get; set; }

    public string Item { get; set; } = string.Empty;
}

public sealed class PedidosDbContext(DbContextOptions<PedidosDbContext> options) : DbContext(options)
{
    public const string Schema = "pedidos";

    public DbSet<Pedido> Pedidos => Set<Pedido>();

    public DbSet<OutboxMensagem> Outbox => Set<OutboxMensagem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<Pedido>().ToTable("pedidos");
        modelBuilder.MapearOutbox(Schema);
    }
}

/// <summary>Efeito de um consumidor: uma linha por evento processado.</summary>
public sealed class Efeito
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PedidoId { get; set; }
}

public abstract class ConsumidorDbContext(DbContextOptions options, string schema) : DbContext(options)
{
    public DbSet<Efeito> Efeitos => Set<Efeito>();

    public DbSet<InboxMensagem> Inbox => Set<InboxMensagem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(schema);
        modelBuilder.Entity<Efeito>().ToTable("efeitos");
        modelBuilder.MapearInbox(schema);
    }
}

public sealed class EstoqueDbContext(DbContextOptions<EstoqueDbContext> options)
    : ConsumidorDbContext(options, "estoque_teste");

public sealed class FiscalDbContext(DbContextOptions<FiscalDbContext> options)
    : ConsumidorDbContext(options, "fiscal_teste");

/// <summary>Comportamento dos handlers controlado pelo teste: falhas forçadas, bloqueio e contagem de execuções.</summary>
public sealed class ControleDosHandlers
{
    private readonly ConcurrentDictionary<string, int> _execucoes = new();
    private readonly ConcurrentDictionary<string, bool> _falhar = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _bloqueios = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _entrou = new();

    public int Execucoes(string consumidor) => _execucoes.GetValueOrDefault(consumidor);

    public void Falhar(string consumidor, bool falhar) => _falhar[consumidor] = falhar;

    private static string Chave(string consumidor, string? item) => item is null ? consumidor : $"{consumidor}:{item}";

    /// <summary>
    /// Os próximos handlers do consumidor (só os do <paramref name="item"/>, se indicado) esperam até
    /// <see cref="Liberar"/> (ou o cancelamento).
    /// </summary>
    public void Bloquear(string consumidor, string? item = null)
    {
        _bloqueios[Chave(consumidor, item)] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _entrou[Chave(consumidor, item)] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void Liberar(string consumidor, string? item = null)
    {
        if (_bloqueios.TryRemove(Chave(consumidor, item), out var bloqueio))
            bloqueio.TrySetResult();
    }

    /// <summary>Completa quando o handler bloqueado começou a rodar.</summary>
    public Task Entrou(string consumidor, string? item = null) => _entrou[Chave(consumidor, item)].Task;

    public async Task ExecutarAsync(string consumidor, string item, CancellationToken ct)
    {
        _execucoes.AddOrUpdate(consumidor, 1, (_, n) => n + 1);
        foreach (var chave in new[] { Chave(consumidor, null), Chave(consumidor, item) })
        {
            if (_entrou.TryGetValue(chave, out var entrou))
                entrou.TrySetResult();
            if (_bloqueios.TryGetValue(chave, out var bloqueio))
                await bloqueio.Task.WaitAsync(ct);
        }

        if (_falhar.GetValueOrDefault(consumidor))
            throw new InvalidOperationException($"falha forçada no {consumidor}");
    }

    public void LiberarTudo()
    {
        foreach (var chave in _bloqueios.Keys)
            if (_bloqueios.TryRemove(chave, out var bloqueio))
                bloqueio.TrySetResult();
    }
}

public sealed class BaixarNoEstoque(EstoqueDbContext contexto, ControleDosHandlers controle)
    : IIntegrationEventHandler<PedidoRegistrado>
{
    public const string Consumidor = "Estoque";

    public async Task HandleAsync(PedidoRegistrado evento, CancellationToken ct)
    {
        contexto.Efeitos.Add(new Efeito { PedidoId = evento.PedidoId });
        await contexto.SaveChangesAsync(ct); // grava antes: a falha/cancelamento depois precisa desfazer
        await controle.ExecutarAsync(Consumidor, evento.Item, ct);
    }
}

public sealed class RegistrarNoFiscal(FiscalDbContext contexto, ControleDosHandlers controle)
    : IIntegrationEventHandler<PedidoRegistrado>
{
    public const string Consumidor = "Fiscal";

    public async Task HandleAsync(PedidoRegistrado evento, CancellationToken ct)
    {
        await controle.ExecutarAsync(Consumidor, evento.Item, ct);
        contexto.Efeitos.Add(new Efeito { PedidoId = evento.PedidoId });
    }
}
