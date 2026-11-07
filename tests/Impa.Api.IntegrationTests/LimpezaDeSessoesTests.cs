using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using Impa.Host.Tarefas;
using Impa.Modules.Acesso.Infrastructure.Persistence;
using Impa.Modules.Acesso.Infrastructure.Sessoes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Impa.Api.IntegrationTests;

/// <summary>
/// API sem a tarefa de limpeza em segundo plano: os testes da consulta chamam a limpeza à mão, sem a execução
/// automática apagar as sessões semeadas antes da hora. <see cref="RetencaoEmDias"/> sobrescreve a configuração.
/// </summary>
public class LimpezaManualFactory : ImpaApiFactory
{
    protected virtual int? RetencaoEmDias => null;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        if (RetencaoEmDias is { } dias)
            builder.UseSetting("Acesso:LimpezaDeSessoes:RetencaoEmDias", dias.ToString(CultureInfo.InvariantCulture));

        builder.ConfigureServices(services =>
        {
            var tarefa = services.Single(d =>
                d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(LimpezaDeSessoesTarefa));
            services.Remove(tarefa);
        });
    }
}

public sealed class LimpezaManualRetencaoDe10DiasFactory : LimpezaManualFactory
{
    protected override int? RetencaoEmDias => 10;
}

/// <summary>API com a tarefa de limpeza ligada e os logs dela guardados (<see cref="Logs"/>).</summary>
public class LimpezaAutomaticaFactory : ImpaApiFactory
{
    public LogsGuardados Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(Logs));
    }
}

/// <summary>Tarefa ligada, com uma limpeza que falha na primeira execução e funciona nas seguintes.</summary>
public sealed class LimpezaComFalhaFactory : LimpezaAutomaticaFactory
{
    public LimpezaQueFalhaNaPrimeira Limpeza { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ILimpezaDeSessoes>();
            services.AddSingleton<ILimpezaDeSessoes>(Limpeza);
        });
    }
}

public sealed class LimpezaQueFalhaNaPrimeira : ILimpezaDeSessoes
{
    private int _execucoes;

    public int Execucoes => Volatile.Read(ref _execucoes);

    public Task<int> ApagarAntigasAsync(TimeSpan retencao, CancellationToken ct) =>
        Interlocked.Increment(ref _execucoes) == 1
            ? throw new InvalidOperationException("Falha simulada na limpeza")
            : Task.FromResult(0);
}

/// <summary>Logs da categoria <see cref="LimpezaDeSessoesTarefa"/>.</summary>
public sealed class LogsGuardados : ILoggerProvider
{
    public sealed record Registro(LogLevel Nivel, string Mensagem, Exception? Excecao);

    private readonly ConcurrentQueue<Registro> _registros = new();

    public IReadOnlyList<Registro> Registros => [.. _registros];

    public ILogger CreateLogger(string categoryName) =>
        categoryName == typeof(LimpezaDeSessoesTarefa).FullName
            ? new Guardador(_registros)
            : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    public void Dispose()
    {
    }

    private sealed class Guardador(ConcurrentQueue<Registro> registros) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            registros.Enqueue(new Registro(logLevel, formatter(state, exception), exception));
    }
}

internal static class SessoesDeTeste
{
    /// <summary>Insere uma sessão direto no banco (do admin), com as datas dadas.</summary>
    public static async Task<Guid> SemearAsync(
        ImpaApiFactory factory, DateTimeOffset criadaEm, DateTimeOffset expiraEm, DateTimeOffset? revogadaEm = null)
    {
        var usuario = await factory.IdDoUsuarioAsync(ImpaApiFactory.AdminUsuario);
        await using var escopo = factory.Services.CreateAsyncScope();
        var banco = escopo.ServiceProvider.GetRequiredService<AcessoDbContext>();
        var sessao = new Sessao(Guid.NewGuid(), usuario, criadaEm, expiraEm, [1, 2, 3]);
        if (revogadaEm is { } revogada)
            sessao.Revogar(revogada);

        banco.Sessoes.Add(sessao);
        await banco.SaveChangesAsync();
        return sessao.Id;
    }

    public static async Task<bool> ExisteAsync(ImpaApiFactory factory, Guid id)
    {
        await using var escopo = factory.Services.CreateAsyncScope();
        return await escopo.ServiceProvider.GetRequiredService<AcessoDbContext>().Sessoes.AnyAsync(s => s.Id == id);
    }

    public static async Task<int> ApagarAntigasAsync(ImpaApiFactory factory, TimeSpan retencao)
    {
        await using var escopo = factory.Services.CreateAsyncScope();
        return await escopo.ServiceProvider.GetRequiredService<ILimpezaDeSessoes>().ApagarAntigasAsync(retencao, CancellationToken.None);
    }

    public static LimpezaDeSessoesOptions Opcoes(ImpaApiFactory factory) =>
        factory.Services.GetRequiredService<IOptions<LimpezaDeSessoesOptions>>().Value;

    /// <summary>Espera a condição ficar verdadeira (a tarefa roda em segundo plano), até 15 s.</summary>
    public static async Task EsperarAsync(Func<Task<bool>> condicao, string oQue)
    {
        var limite = DateTime.UtcNow.AddSeconds(15);
        while (!await condicao())
        {
            Assert.True(DateTime.UtcNow < limite, $"Tempo esgotado esperando: {oQue}");
            await Task.Delay(50);
        }
    }
}

/// <summary>Story 1-11, T3 — consulta de limpeza: o que é apagado e o que fica (CA-1, CA-2, CA-4).</summary>
public class LimpezaDeSessoesTests(LimpezaManualFactory factory) : IClassFixture<LimpezaManualFactory>
{
    [Fact]
    public async Task Sessoes_terminadas_ha_mais_de_30_dias_sao_apagadas_e_as_recentes_e_validas_ficam()
    {
        _ = factory.CriarCliente();
        var agora = factory.Clock.GetUtcNow();
        var dias = (double d) => agora.AddDays(d);

        // CA-1: revogada há 31 dias (o fim do tíquete, há 29, não salva) e vencida há 31 dias — apagadas.
        var revogadaHa31 = await SessoesDeTeste.SemearAsync(factory, dias(-32), dias(-29), revogadaEm: dias(-31));
        var vencidaHa31 = await SessoesDeTeste.SemearAsync(factory, dias(-31).AddHours(-12), dias(-31));

        // CA-1: terminadas há 30 dias ou menos ficam, como registro de acesso.
        var revogadaHa29 = await SessoesDeTeste.SemearAsync(factory, dias(-29).AddHours(-1), dias(-29).AddHours(11), revogadaEm: dias(-29));
        var vencidaHa29 = await SessoesDeTeste.SemearAsync(factory, dias(-29).AddHours(-12), dias(-29));
        var vencidaQuase30 = await SessoesDeTeste.SemearAsync(factory, dias(-30).AddHours(-12), dias(-30).AddMinutes(1));

        // CA-2: válida (não revogada, fim no futuro) nunca é apagada, por mais antiga que seja.
        var validaAntiga = await SessoesDeTeste.SemearAsync(factory, dias(-90), agora.AddHours(1));

        var apagadas = await SessoesDeTeste.ApagarAntigasAsync(factory, TimeSpan.FromDays(30));

        Assert.Equal(2, apagadas);
        Assert.False(await SessoesDeTeste.ExisteAsync(factory, revogadaHa31));
        Assert.False(await SessoesDeTeste.ExisteAsync(factory, vencidaHa31));
        Assert.True(await SessoesDeTeste.ExisteAsync(factory, revogadaHa29));
        Assert.True(await SessoesDeTeste.ExisteAsync(factory, vencidaHa29));
        Assert.True(await SessoesDeTeste.ExisteAsync(factory, vencidaQuase30));
        Assert.True(await SessoesDeTeste.ExisteAsync(factory, validaAntiga));

        // Nada mais para apagar: a segunda execução devolve zero.
        Assert.Equal(0, await SessoesDeTeste.ApagarAntigasAsync(factory, TimeSpan.FromDays(30)));
    }

    [Fact]
    public async Task Sessao_aberta_por_login_nao_e_apagada()
    {
        var cliente = await ClienteApi.AbrirAsync(factory);
        Assert.Equal(HttpStatusCode.NoContent,
            (await cliente.EntrarAsync(ImpaApiFactory.AdminUsuario, ImpaApiFactory.AdminSenha)).StatusCode);

        await SessoesDeTeste.ApagarAntigasAsync(factory, TimeSpan.FromDays(30));

        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public void Retencao_e_intervalo_padrao_sao_30_dias_e_24_horas()
    {
        _ = factory.CriarCliente();
        var opcoes = SessoesDeTeste.Opcoes(factory);

        Assert.Equal(TimeSpan.FromDays(30), opcoes.Retencao);
        Assert.Equal(TimeSpan.FromHours(24), opcoes.Intervalo);
        Assert.Equal(30, new LimpezaDeSessoesOptions().RetencaoEmDias);
        Assert.Equal(24, new LimpezaDeSessoesOptions().IntervaloEmHoras);
    }
}

/// <summary>Story 1-11, T3 — com outra retenção configurada, a regra segue o valor configurado (CA-4).</summary>
public class LimpezaDeSessoesRetencaoConfiguradaTests(LimpezaManualRetencaoDe10DiasFactory factory)
    : IClassFixture<LimpezaManualRetencaoDe10DiasFactory>
{
    [Fact]
    public async Task Retencao_configurada_de_10_dias_apaga_as_terminadas_ha_mais_de_10_dias()
    {
        _ = factory.CriarCliente();
        var opcoes = SessoesDeTeste.Opcoes(factory);
        Assert.Equal(TimeSpan.FromDays(10), opcoes.Retencao);

        var agora = factory.Clock.GetUtcNow();
        var vencidaHa11 = await SessoesDeTeste.SemearAsync(factory, agora.AddDays(-11).AddHours(-12), agora.AddDays(-11));
        var vencidaHa9 = await SessoesDeTeste.SemearAsync(factory, agora.AddDays(-9).AddHours(-12), agora.AddDays(-9));

        Assert.Equal(1, await SessoesDeTeste.ApagarAntigasAsync(factory, opcoes.Retencao));
        Assert.False(await SessoesDeTeste.ExisteAsync(factory, vencidaHa11));
        Assert.True(await SessoesDeTeste.ExisteAsync(factory, vencidaHa9));
    }
}

/// <summary>Story 1-11, T4 — a limpeza roda sozinha: ao subir a API e a cada intervalo (CA-3).</summary>
public class LimpezaDeSessoesTarefaTests(LimpezaAutomaticaFactory factory) : IClassFixture<LimpezaAutomaticaFactory>
{
    private int ExecucoesLogadas() =>
        factory.Logs.Registros.Count(r => r.Nivel == LogLevel.Information && r.Mensagem.Contains("sessões apagadas"));

    [Fact]
    public async Task Limpeza_roda_ao_subir_a_api_e_de_novo_a_cada_intervalo()
    {
        _ = factory.CriarCliente();
        await SessoesDeTeste.EsperarAsync(() => Task.FromResult(ExecucoesLogadas() >= 1), "execução ao subir a API");

        // Uma sessão antiga semeada depois da primeira execução fica até a próxima.
        var agora = factory.Clock.GetUtcNow();
        var antiga = await SessoesDeTeste.SemearAsync(factory, agora.AddDays(-40), agora.AddDays(-40).AddHours(12));
        var antes = ExecucoesLogadas();

        factory.Clock.Advance(TimeSpan.FromHours(23));
        await Task.Delay(300);
        Assert.True(await SessoesDeTeste.ExisteAsync(factory, antiga));

        factory.Clock.Advance(TimeSpan.FromHours(1)); // completa o intervalo de 24 h
        await SessoesDeTeste.EsperarAsync(async () => !await SessoesDeTeste.ExisteAsync(factory, antiga), "sessão antiga apagada");
        await SessoesDeTeste.EsperarAsync(() => Task.FromResult(ExecucoesLogadas() > antes), "log da segunda execução");
        Assert.Contains(factory.Logs.Registros, r => r.Mensagem.Contains("1 sessões apagadas"));
    }

    [Fact]
    public async Task Limpeza_roda_ao_iniciar_sem_ser_chamada_a_mao()
    {
        _ = factory.CriarCliente();
        await SessoesDeTeste.EsperarAsync(() => Task.FromResult(ExecucoesLogadas() >= 1), "execução ao subir a API");

        var agora = factory.Clock.GetUtcNow();
        var antiga = await SessoesDeTeste.SemearAsync(factory, agora.AddDays(-50), agora.AddDays(-50).AddHours(12));

        // Uma nova instância da API sobre o mesmo banco: a execução ao iniciar apaga a sessão antiga.
        await using var outraApi = factory.WithWebHostBuilder(_ => { });
        _ = outraApi.CreateClient();

        await SessoesDeTeste.EsperarAsync(async () => !await SessoesDeTeste.ExisteAsync(factory, antiga), "sessão antiga apagada ao iniciar");
    }
}

/// <summary>Story 1-11, T4 — uma falha numa execução é logada e não impede a próxima nem derruba a API (CA-3).</summary>
public class LimpezaDeSessoesFalhaTests(LimpezaComFalhaFactory factory) : IClassFixture<LimpezaComFalhaFactory>
{
    [Fact]
    public async Task Falha_numa_execucao_e_logada_e_a_proxima_execucao_acontece()
    {
        var cliente = factory.CriarCliente();
        await SessoesDeTeste.EsperarAsync(() => Task.FromResult(factory.Limpeza.Execucoes >= 1), "primeira execução");
        await SessoesDeTeste.EsperarAsync(
            () => Task.FromResult(factory.Logs.Registros.Any(r => r.Nivel == LogLevel.Error && r.Excecao is InvalidOperationException)),
            "log da falha");

        // A API segue no ar.
        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/api/health")).StatusCode);

        factory.Clock.Advance(SessoesDeTeste.Opcoes(factory).Intervalo);
        await SessoesDeTeste.EsperarAsync(() => Task.FromResult(factory.Limpeza.Execucoes >= 2), "segunda execução");
        await SessoesDeTeste.EsperarAsync(
            () => Task.FromResult(factory.Logs.Registros.Any(r => r.Nivel == LogLevel.Information && r.Mensagem.Contains("0 sessões apagadas"))),
            "log da segunda execução");
    }
}
