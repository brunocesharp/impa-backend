using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Impa.Modules.Acesso.Application.Perfis;
using Impa.Modules.Acesso.Contracts;
using Impa.Modules.Configuracao.Application.Notificacoes;
using Impa.Modules.Configuracao.Contracts.Notificacoes;
using Impa.Modules.Configuracao.Infrastructure;
using Impa.Modules.Configuracao.Infrastructure.Persistence;
using Impa.Shared.Messaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Impa.Api.IntegrationTests;

/// <summary>
/// Fato de teste com o texto do cenário 2.5: a notificação real "sobre Caneta azul 0.7" é a de estoque negativo,
/// que só existe no épico 7 (story 7-2).
/// </summary>
[EventoDeIntegracao("teste.saldo-ficou-negativo", 1)]
public sealed record SaldoFicouNegativoDeTeste(string Item, int Saldo) : IntegrationEvent;

public sealed class NotificarSaldoNegativoDeTeste(CriacaoDeNotificacoes criacao)
    : GeradorDeNotificacao<SaldoFicouNegativoDeTeste>(criacao)
{
    public const string TipoDaNotificacao = "teste.saldo-negativo";

    protected override string Tipo => TipoDaNotificacao;

    protected override string Titulo(SaldoFicouNegativoDeTeste evento) => "Saldo negativo";

    protected override string Texto(SaldoFicouNegativoDeTeste evento) => $"{evento.Item} ficou com saldo {evento.Saldo}";
}

/// <summary>API com o handler do fato de teste registrado, como as stories 7-2 e 9-3 vão registrar os seus.</summary>
public sealed class ImpaApiComNotificacaoDeTeste : ImpaApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
            services.AddGeradorDeNotificacao<SaldoFicouNegativoDeTeste, NotificarSaldoNegativoDeTeste>());
    }
}

/// <summary>Story 4.2, T5 — central de notificações: cenário 2.5 (CA-1) e RN-2.3 (CA-2), de ponta a ponta.</summary>
public class NotificacoesTests(ImpaApiComNotificacaoDeTeste factory) : IClassFixture<ImpaApiComNotificacaoDeTeste>
{
    private const string Contagem = "/api/notificacoes/nao-lidas/contagem";

    private sealed record ItemResposta(Guid Id, string Tipo, string Titulo, string Texto, bool Lida);

    private sealed record PaginaResposta(List<ItemResposta> Itens, int Pagina, int TamanhoDaPagina, int Total);

    private async Task<ClienteApi> EntrarAsync(string usuario, string senha)
    {
        var cliente = await ClienteApi.AbrirAsync(factory);
        Assert.Equal(HttpStatusCode.NoContent, (await cliente.EntrarAsync(usuario, senha)).StatusCode);
        return cliente;
    }

    private static async Task<int> NaoLidasAsync(ClienteApi cliente)
    {
        var resposta = await cliente.GetAsync(Contagem);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        return JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement.GetProperty("naoLidas").GetInt32();
    }

    private static async Task<PaginaResposta> ListarAsync(ClienteApi cliente, string consulta = "")
    {
        var resposta = await cliente.GetAsync($"/api/notificacoes{consulta}");
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        return (await resposta.Content.ReadFromJsonAsync<PaginaResposta>())!;
    }

    /// <summary>Um módulo publica o fato no próprio Outbox, na transação dos dados (aqui, o do Configuração).</summary>
    private async Task<SaldoFicouNegativoDeTeste> PublicarFatoAsync(string item, int saldo)
    {
        await using var escopo = factory.Services.CreateAsyncScope();
        var contexto = escopo.ServiceProvider.GetRequiredService<ConfiguracaoDbContext>();
        var evento = new SaldoFicouNegativoDeTeste(item, saldo);
        escopo.ServiceProvider.GetRequiredService<IEventPublisher<ConfiguracaoDbContext>>().Publicar(evento);
        await contexto.SaveChangesAsync();
        return evento;
    }

    private async Task<T> NoBancoAsync<T>(Func<ConfiguracaoDbContext, Task<T>> consulta)
    {
        await using var escopo = factory.Services.CreateAsyncScope();
        return await consulta(escopo.ServiceProvider.GetRequiredService<ConfiguracaoDbContext>());
    }

    private Task<int> NotificacoesComTextoAsync(string texto) =>
        NoBancoAsync(c => c.Notificacoes.CountAsync(n => n.Texto == texto));

    private static async Task AteAsync(Func<Task<bool>> condicao, string mensagem)
    {
        var fim = DateTime.UtcNow.AddSeconds(20);
        while (!await condicao())
        {
            if (DateTime.UtcNow > fim)
                Assert.Fail(mensagem);
            await Task.Delay(50);
        }
    }

    private Task EntregueAsync(Guid eventoId) =>
        AteAsync(() => NoBancoAsync(c => c.Set<OutboxMensagem>()
            .AnyAsync(m => m.Id == eventoId && m.Situacao == SituacaoDoEvento.Entregue && m.EntregueEm != null)),
            "fato não entregue ao Configuração");

    [Fact]
    public async Task Cenario_2_5_lida_para_um_usuario_so()
    {
        // Dado que Carla e Diego, administradores, têm 1 notificação não lida cada um sobre "Caneta azul 0.7"
        await factory.CriarUsuarioAsync("carla", "carla-2026", "Carla", PerfisPadrao.Administrador.Nome);
        await factory.CriarUsuarioAsync("diego", "diego-2026", "Diego", PerfisPadrao.Administrador.Nome);
        var carla = await EntrarAsync("carla", "carla-2026");
        var diego = await EntrarAsync("diego", "diego-2026");

        var fato = await PublicarFatoAsync("Caneta azul 0.7", -1);
        await EntregueAsync(fato.Id);
        Assert.Equal(1, await NaoLidasAsync(carla));
        Assert.Equal(1, await NaoLidasAsync(diego));
        var daCarla = Assert.Single((await ListarAsync(carla)).Itens);
        Assert.Equal("Caneta azul 0.7 ficou com saldo -1", daCarla.Texto);
        Assert.Equal(NotificarSaldoNegativoDeTeste.TipoDaNotificacao, daCarla.Tipo);
        Assert.False(daCarla.Lida);

        // Quando Carla marca a notificação como lida
        Assert.Equal(HttpStatusCode.NoContent, (await carla.PostAsync($"/api/notificacoes/{daCarla.Id}/lida")).StatusCode);

        // Então o contador de Carla mostra 0 não lidas E o contador de Diego continua mostrando 1
        Assert.Equal(0, await NaoLidasAsync(carla));
        Assert.Equal(1, await NaoLidasAsync(diego));
        Assert.True(Assert.Single((await ListarAsync(carla)).Itens).Lida);
        Assert.False(Assert.Single((await ListarAsync(diego)).Itens).Lida);

        // Marcar de novo não muda nada.
        Assert.Equal(HttpStatusCode.NoContent, (await carla.PostAsync($"/api/notificacoes/{daCarla.Id}/lida")).StatusCode);
        Assert.Equal(1, await NaoLidasAsync(diego));
    }

    [Fact]
    public async Task Operador_de_caixa_nao_recebe_a_notificacao_e_recebe_403_nos_endpoints()
    {
        var anaId = await factory.CriarUsuarioAsync("ana", "balcao2026", "Ana", PerfisPadrao.OperadorDeCaixa.Nome);
        var ana = await EntrarAsync("ana", "balcao2026");

        var fato = await PublicarFatoAsync("Borracha branca", -3);
        await EntregueAsync(fato.Id);

        var destinatarios = await NoBancoAsync(c => c.NotificacoesDestinatarios
            .Where(d => c.Notificacoes.Any(n => n.Id == d.NotificacaoId && n.Texto == "Borracha branca ficou com saldo -3"))
            .Select(d => d.UsuarioId).ToListAsync());
        Assert.NotEmpty(destinatarios); // o administrador recebe
        Assert.DoesNotContain(anaId, destinatarios);
        Assert.Contains(await factory.IdDoUsuarioAsync(ImpaApiFactory.AdminUsuario), destinatarios);

        Assert.Equal(HttpStatusCode.Forbidden, (await ana.GetAsync("/api/notificacoes")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.GetAsync(Contagem)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.PostAsync($"/api/notificacoes/{Guid.NewGuid()}/lida")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.PostAsync("/api/notificacoes/lidas")).StatusCode);
    }

    [Fact]
    public async Task Sem_sessao_responde_401()
    {
        var anonimo = await ClienteApi.AbrirAsync(factory);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync(Contagem)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PostAsync("/api/notificacoes/lidas")).StatusCode);
    }

    [Fact]
    public async Task Permissao_nova_esta_no_catalogo_do_administrador_e_no_auth_me()
    {
        var admin = await EntrarAsync(ImpaApiFactory.AdminUsuario, ImpaApiFactory.AdminSenha);

        var me = JsonDocument.Parse(await (await admin.GetAsync("/api/auth/me")).Content.ReadAsStringAsync()).RootElement;

        Assert.Contains(Permissoes.NotificacoesReceber,
            me.GetProperty("permissoes").EnumerateArray().Select(p => p.GetString()));
        Assert.DoesNotContain(Permissoes.NotificacoesReceber, PerfisPadrao.OperadorDeCaixa.Permissoes);
    }

    [Fact]
    public async Task Fato_entregue_duas_vezes_gera_uma_notificacao_so()
    {
        var fato = await PublicarFatoAsync("Lápis HB", -1);
        await EntregueAsync(fato.Id);
        Assert.Equal(1, await NotificacoesComTextoAsync("Lápis HB ficou com saldo -1"));

        // Reentrega (ex.: a API caiu antes de confirmar a entrega): o evento volta a pendente no Outbox.
        await NoBancoAsync(c => c.Database.ExecuteSqlAsync(
            $"UPDATE configuracao.outbox SET situacao = 'Pendente', entregue_em = NULL WHERE id = {fato.Id}"));
        factory.Services.GetRequiredService<SinalDoDespachante>().Acordar();
        await EntregueAsync(fato.Id);

        Assert.Equal(1, await NotificacoesComTextoAsync("Lápis HB ficou com saldo -1"));
        Assert.Equal(1, await NoBancoAsync(c => c.Set<InboxMensagem>().CountAsync(i => i.EventoId == fato.Id)));
    }

    [Fact]
    public async Task Criar_a_notificacao_publica_NotificacaoCriada_com_tipo_e_destinatarios()
    {
        var fato = await PublicarFatoAsync("Cola bastão", -2);
        await EntregueAsync(fato.Id);

        var conteudos = await NoBancoAsync(c => c.Set<OutboxMensagem>()
            .Where(m => m.Tipo == "configuracao.notificacao-criada").Select(m => m.Conteudo).ToListAsync());
        var criada = conteudos
            .Select(json => JsonSerializer.Deserialize<NotificacaoCriada>(json, JsonSerializerOptions.Web)!)
            .Single(e => e.Texto == "Cola bastão ficou com saldo -2");

        Assert.Equal(NotificarSaldoNegativoDeTeste.TipoDaNotificacao, criada.TipoDaNotificacao);
        Assert.Contains(await factory.IdDoUsuarioAsync(ImpaApiFactory.AdminUsuario), criada.Destinatarios);
        Assert.Equal(1, await NoBancoAsync(c => c.Notificacoes.CountAsync(n => n.Id == criada.NotificacaoId)));
    }

    [Fact]
    public async Task Marcar_todas_e_paginacao_mais_recentes_primeiro()
    {
        await factory.CriarUsuarioAsync("elisa", "elisa-2026", "Elisa", PerfisPadrao.Administrador.Nome);
        var elisa = await EntrarAsync("elisa", "elisa-2026");
        var antes = await NaoLidasAsync(elisa);
        foreach (var (item, saldo) in new[] { ("Clips", -1), ("Grampo", -2), ("Fita", -3) })
        {
            var fato = await PublicarFatoAsync(item, saldo);
            await EntregueAsync(fato.Id);
            factory.Clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(antes + 3, await NaoLidasAsync(elisa));
        var primeira = await ListarAsync(elisa, "?pagina=1&tamanho=2");
        Assert.Equal(antes + 3, primeira.Total);
        Assert.Equal(["Fita ficou com saldo -3", "Grampo ficou com saldo -2"], primeira.Itens.Select(i => i.Texto));
        Assert.Equal(HttpStatusCode.BadRequest, (await elisa.GetAsync("/api/notificacoes?tamanho=0")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await elisa.PostAsync("/api/notificacoes/lidas")).StatusCode);

        Assert.Equal(0, await NaoLidasAsync(elisa));
        Assert.Equal(HttpStatusCode.NotFound, (await elisa.PostAsync($"/api/notificacoes/{Guid.NewGuid()}/lida")).StatusCode);
    }
}
