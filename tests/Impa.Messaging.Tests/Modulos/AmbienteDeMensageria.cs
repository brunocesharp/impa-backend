using Impa.Host.Messaging;
using Impa.Shared.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Impa.Messaging.Tests.Modulos;

/// <summary>Um PostgreSQL em container para todos os testes de mensageria; cada teste usa um banco novo.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("impa")
        .WithUsername("impa")
        .WithPassword("impa")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    public async Task<string> NovoBancoAsync()
    {
        var nome = $"t_{Guid.NewGuid():N}";
        await using (var conexao = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await conexao.OpenAsync();
            await using var comando = new NpgsqlCommand($"CREATE DATABASE \"{nome}\"", conexao);
            await comando.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = nome }.ConnectionString;
    }
}

[CollectionDefinition(Nome)]
public sealed class ColecaoPostgres : ICollectionFixture<PostgresFixture>
{
    public const string Nome = "postgres";
}

/// <summary>
/// Os módulos de teste num host genérico com o despachante real (como o Host da API registra).
/// Intervalos curtos para os testes; <see cref="IniciarAsync"/> sobe o host (um "processo" da API).
/// </summary>
public sealed class AmbienteDeMensageria : IAsyncDisposable
{
    public const int LimiteDeTentativas = 10;

    private readonly string _connectionString;
    private readonly ControleDosHandlers _controle;
    private readonly Dictionary<string, string?> _configuracao;
    private readonly TimeProvider _relogio;
    private IHost? _host;

    private AmbienteDeMensageria(
        string connectionString, ControleDosHandlers controle, Dictionary<string, string?> configuracao, TimeProvider relogio)
    {
        _connectionString = connectionString;
        _controle = controle;
        _configuracao = configuracao;
        _relogio = relogio;
    }

    /// <summary>Intervalo do ambiente de teste depois de <paramref name="tentativas"/> falhas (o mesmo da configuração).</summary>
    public static TimeSpan IntervaloAposTentativa(int tentativas) => TimeSpan.FromMilliseconds(20 + (tentativas - 1) * 10);

    public ControleDosHandlers Controle => _controle;

    public IServiceProvider Servicos => _host?.Services ?? throw new InvalidOperationException("Host não iniciado.");

    public static async Task<AmbienteDeMensageria> CriarAsync(
        PostgresFixture postgres, TimeSpan? intervaloDeVerificacao = null, TimeProvider? relogio = null)
    {
        var configuracao = new Dictionary<string, string?>
        {
            ["Mensageria:IntervaloDeVerificacao"] = (intervaloDeVerificacao ?? TimeSpan.FromSeconds(5)).ToString(),
            ["Mensageria:LimiteDeTentativas"] = LimiteDeTentativas.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        for (var i = 0; i < LimiteDeTentativas; i++)
            configuracao[$"Mensageria:IntervalosDeNovaTentativa:{i}"] = IntervaloAposTentativa(i + 1).ToString();

        var ambiente = new AmbienteDeMensageria(
            await postgres.NovoBancoAsync(), new ControleDosHandlers(), configuracao, relogio ?? TimeProvider.System);
        using var hostDeCriacao = ambiente.Construir(); // não iniciado: só para criar as tabelas
        await using var provedor = hostDeCriacao.Services.CreateAsyncScope();
        foreach (var tipo in new[] { typeof(PedidosDbContext), typeof(EstoqueDbContext), typeof(FiscalDbContext) })
        {
            var contexto = (DbContext)provedor.ServiceProvider.GetRequiredService(tipo);
            var criador = contexto.GetService<IRelationalDatabaseCreator>();
            if (!await criador.ExistsAsync())
                await criador.CreateAsync();
            await criador.CreateTablesAsync();
        }

        return ambiente;
    }

    private IHost Construir()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(_configuracao);
        var servicos = builder.Services;

        servicos.AddSingleton(_relogio); // antes da mensageria, que só registra o relógio do sistema se faltar
        servicos.AddSingleton(_controle);
        servicos.AddDbContext<PedidosDbContext>((sp, o) => o.UseNpgsql(_connectionString).UsarOutbox(sp));
        servicos.AddDbContext<EstoqueDbContext>((sp, o) => o.UseNpgsql(_connectionString).UsarOutbox(sp));
        servicos.AddDbContext<FiscalDbContext>((sp, o) => o.UseNpgsql(_connectionString).UsarOutbox(sp));

        servicos.AddOutbox<PedidosDbContext>();
        servicos.AddIntegrationEventHandler<PedidoRegistrado, BaixarNoEstoque, EstoqueDbContext>(BaixarNoEstoque.Consumidor);
        servicos.AddIntegrationEventHandler<PedidoRegistrado, RegistrarNoFiscal, FiscalDbContext>(RegistrarNoFiscal.Consumidor);

        servicos.AddDespachanteDeEventos(builder.Configuration);
        return builder.Build();
    }

    /// <summary>Sobe um host novo (o anterior precisa ter sido parado): simula o reinício da API.</summary>
    public async Task IniciarAsync()
    {
        if (_host is not null)
            throw new InvalidOperationException("Pare o host antes de iniciar outro.");
        _host = Construir();
        await _host.StartAsync();
    }

    public async Task PararAsync()
    {
        if (_host is null)
            return;
        await _host.StopAsync();
        _host.Dispose();
        _host = null;
    }

    /// <summary>Registra um pedido e publica <see cref="PedidoRegistrado"/> na mesma transação, como um módulo faria.</summary>
    public async Task<PedidoRegistrado> RegistrarPedidoAsync(string item = "caneta", bool desfazer = false)
    {
        await using var escopo = Servicos.CreateAsyncScope();
        var contexto = escopo.ServiceProvider.GetRequiredService<PedidosDbContext>();
        var publicador = escopo.ServiceProvider.GetRequiredService<IEventPublisher<PedidosDbContext>>();

        await using var transacao = await contexto.Database.BeginTransactionAsync();
        var pedido = new Pedido { Id = Guid.NewGuid(), Item = item };
        contexto.Pedidos.Add(pedido);
        var evento = new PedidoRegistrado(pedido.Id, item);
        publicador.Publicar(evento);
        await contexto.SaveChangesAsync();

        if (desfazer)
            await transacao.RollbackAsync();
        else
            await transacao.CommitAsync();

        return evento;
    }

    public async Task<T> NoContextoAsync<TContexto, T>(Func<TContexto, Task<T>> consulta)
        where TContexto : DbContext
    {
        await using var escopo = Servicos.CreateAsyncScope();
        return await consulta(escopo.ServiceProvider.GetRequiredService<TContexto>());
    }

    public Task<OutboxMensagem?> OutboxAsync(Guid eventoId) =>
        NoContextoAsync<PedidosDbContext, OutboxMensagem?>(c => c.Outbox.AsNoTracking().SingleOrDefaultAsync(m => m.Id == eventoId));

    public Task<int> EfeitosAsync<TContexto>(Guid pedidoId) where TContexto : ConsumidorDbContext =>
        NoContextoAsync<TContexto, int>(c => c.Efeitos.CountAsync(e => e.PedidoId == pedidoId));

    /// <summary>Espera a condição (consulta ao banco) ficar verdadeira, até o limite.</summary>
    public static async Task AteAsync(Func<Task<bool>> condicao, TimeSpan? limite = null, string? mensagem = null)
    {
        var fim = DateTime.UtcNow + (limite ?? TimeSpan.FromSeconds(15));
        while (!await condicao())
        {
            if (DateTime.UtcNow > fim)
                Assert.Fail(mensagem ?? "condição não atendida no tempo limite");
            await Task.Delay(25);
        }
    }

    public Task EntregueAsync(Guid eventoId, TimeSpan? limite = null) =>
        AteAsync(async () => (await OutboxAsync(eventoId))?.Situacao == SituacaoDoEvento.Entregue, limite, "evento não entregue");

    public async ValueTask DisposeAsync()
    {
        _controle.LiberarTudo();
        await PararAsync();
    }
}
