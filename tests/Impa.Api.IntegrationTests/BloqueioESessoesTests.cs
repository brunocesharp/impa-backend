using System.Net;
using System.Net.Http.Json;
using Impa.Modules.Acesso.Application.Perfis;
using Impa.Modules.Acesso.Infrastructure.Identity;
using Impa.Modules.Acesso.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Impa.Api.IntegrationTests;

/// <summary>Story 1-2, T6 — bloqueio por tentativas, desativação e sessões no servidor (CA-1 a CA-4).</summary>
public class BloqueioESessoesTests(ImpaApiFactory factory) : IClassFixture<ImpaApiFactory>
{
    private const string Senha = "senha-certa-123";
    private const string MensagemCredencialInvalida = "Usuário ou senha inválidos";
    private const string MensagemBloqueado = "Usuário bloqueado temporariamente. Tente novamente em 15 minutos";

    private sealed record ErroResposta(string Mensagem, string? Codigo);

    private async Task<string> NovoUsuarioAsync(string prefixo)
    {
        var login = $"{prefixo}-{Guid.NewGuid():N}"[..20];
        await factory.CriarUsuarioAsync(login, Senha, prefixo, PerfisPadrao.OperadorDeCaixa.Nome);
        return login;
    }

    private async Task<Usuario> BuscarAsync(string login)
    {
        await using var escopo = factory.Services.CreateAsyncScope();
        return (await escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>().FindByNameAsync(login))!;
    }

    private static async Task ErrarAsync(ClienteApi cliente, string login, int vezes)
    {
        for (var i = 0; i < vezes; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.EntrarAsync(login, "errada-123")).StatusCode);
    }

    private async Task<ClienteApi> AdminAsync()
    {
        var admin = await ClienteApi.AbrirAsync(factory);
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.EntrarAsync(ImpaApiFactory.AdminUsuario, ImpaApiFactory.AdminSenha)).StatusCode);
        return admin;
    }

    // ---- CA-1, CA-3: bloqueio por tentativas ----

    [Fact]
    public async Task Quatro_senhas_erradas_nao_bloqueiam()
    {
        var login = await NovoUsuarioAsync("quatro");
        var cliente = await ClienteApi.AbrirAsync(factory);

        await ErrarAsync(cliente, login, 4);

        Assert.Equal(HttpStatusCode.NoContent, (await cliente.EntrarAsync(login, Senha)).StatusCode);
    }

    [Fact]
    public async Task Cenario_1_3_quinta_senha_errada_bloqueia_por_15_minutos_e_a_senha_correta_e_recusada()
    {
        var login = await NovoUsuarioAsync("ana");
        var cliente = await ClienteApi.AbrirAsync(factory);
        await ErrarAsync(cliente, login, 4);

        var agora = factory.Clock.GetUtcNow(); // relógio da API nos testes (parado)
        var quinta = await cliente.EntrarAsync(login, "errada-123");

        Assert.Equal(HttpStatusCode.Unauthorized, quinta.StatusCode);
        var erro = await quinta.Content.ReadFromJsonAsync<ErroResposta>();
        Assert.Equal(MensagemBloqueado, erro!.Mensagem);
        Assert.Equal("bloqueado", erro.Codigo);

        var usuario = await BuscarAsync(login);
        Assert.NotNull(usuario.LockoutEnd);
        Assert.InRange(usuario.LockoutEnd!.Value, agora.AddMinutes(15).AddSeconds(-1), agora.AddMinutes(15).AddSeconds(1));

        var correta = await cliente.EntrarAsync(login, Senha);
        Assert.Equal(HttpStatusCode.Unauthorized, correta.StatusCode);
        Assert.Equal(MensagemBloqueado, (await correta.Content.ReadFromJsonAsync<ErroResposta>())!.Mensagem);
        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Passados_os_15_minutos_a_senha_correta_entra()
    {
        var login = await NovoUsuarioAsync("desbloq");
        var cliente = await ClienteApi.AbrirAsync(factory);
        await ErrarAsync(cliente, login, 5);
        var inicio = factory.Clock.GetUtcNow();

        // O FakeTimeProvider só anda para a frente; os outros testes da classe usam tempos relativos a ele.
        factory.Clock.SetUtcNow(inicio.AddMinutes(14));
        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.EntrarAsync(login, Senha)).StatusCode);

        factory.Clock.SetUtcNow(inicio.AddMinutes(15).AddSeconds(1));
        Assert.Equal(HttpStatusCode.NoContent, (await cliente.EntrarAsync(login, Senha)).StatusCode);
    }

    [Fact]
    public async Task Conta_bloqueada_tambem_calcula_o_hash_da_senha()
    {
        var login = await NovoUsuarioAsync("hash");
        var cliente = await ClienteApi.AbrirAsync(factory);
        await ErrarAsync(cliente, login, 5);

        factory.Hasher.Zerar();
        await cliente.EntrarAsync(login, Senha);

        Assert.Equal(1, factory.Hasher.Conferencias);
    }

    [Fact]
    public async Task Login_correto_apos_erros_zera_a_contagem()
    {
        var login = await NovoUsuarioAsync("zera");
        var cliente = await ClienteApi.AbrirAsync(factory);

        await ErrarAsync(cliente, login, 4);
        Assert.Equal(HttpStatusCode.NoContent, (await cliente.EntrarAsync(login, Senha)).StatusCode);
        Assert.Equal(0, (await BuscarAsync(login)).AccessFailedCount);

        // Mais 4 erros não somam aos de antes: ainda entra.
        var outro = await ClienteApi.AbrirAsync(factory);
        await ErrarAsync(outro, login, 4);
        Assert.Equal(HttpStatusCode.NoContent, (await outro.EntrarAsync(login, Senha)).StatusCode);
    }

    [Fact]
    public async Task R2_senhas_erradas_em_paralelo_nao_passam_do_limite_de_5_e_nao_dao_500()
    {
        var login = await NovoUsuarioAsync("rajada");
        var clientes = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => ClienteApi.AbrirAsync(factory)));

        var respostas = await Task.WhenAll(clientes.Select((c, i) => c.EntrarAsync(login, $"errada-{i}")));

        Assert.All(respostas, r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
        var mensagens = await Task.WhenAll(respostas.Select(async r => (await r.Content.ReadFromJsonAsync<ErroResposta>())!.Mensagem));
        Assert.Equal(4, mensagens.Count(m => m == MensagemCredencialInvalida));
        Assert.Equal(6, mensagens.Count(m => m == MensagemBloqueado));
        Assert.NotNull((await BuscarAsync(login)).LockoutEnd);
    }

    [Fact]
    public async Task Credencial_invalida_continua_sem_codigo()
    {
        var resposta = await (await ClienteApi.AbrirAsync(factory)).EntrarAsync("nao-existe", "errada-123");

        Assert.Equal("""{"mensagem":"Usuário ou senha inválidos"}""", await resposta.Content.ReadAsStringAsync());
    }

    // ---- CA-2, CA-4: desativação ----

    [Fact]
    public async Task Cenario_1_4_desativar_encerra_a_sessao_aberta_e_recusa_novos_logins()
    {
        var login = await NovoUsuarioAsync("bruno");
        var bruno = await ClienteApi.AbrirAsync(factory);
        Assert.Equal(HttpStatusCode.NoContent, (await bruno.EntrarAsync(login, Senha)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bruno.GetAsync("/api/auth/me")).StatusCode);
        var id = (await BuscarAsync(login)).Id;

        var admin = await AdminAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/usuarios/{id}/desativar")).StatusCode);

        // A próxima requisição de Bruno, com o mesmo cookie, já não tem sessão.
        Assert.Equal(HttpStatusCode.Unauthorized, (await bruno.GetAsync("/api/auth/me")).StatusCode);

        var novoLogin = await (await ClienteApi.AbrirAsync(factory)).EntrarAsync(login, Senha);
        Assert.Equal(HttpStatusCode.Unauthorized, novoLogin.StatusCode);
        Assert.Equal(MensagemCredencialInvalida, (await novoLogin.Content.ReadFromJsonAsync<ErroResposta>())!.Mensagem);

        await using var escopo = factory.Services.CreateAsyncScope();
        var banco = escopo.ServiceProvider.GetRequiredService<AcessoDbContext>();
        Assert.False((await banco.Users.SingleAsync(u => u.Id == id)).Ativo);
        Assert.All(await banco.Sessoes.Where(s => s.UsuarioId == id).ToListAsync(), s => Assert.NotNull(s.RevogadaEm));
    }

    [Fact]
    public async Task Desativar_exige_a_permissao_usuarios_gerenciar()
    {
        var alvo = (await BuscarAsync(await NovoUsuarioAsync("alvo"))).Id;
        var operador = await NovoUsuarioAsync("operador");
        var cliente = await ClienteApi.AbrirAsync(factory);
        await cliente.EntrarAsync(operador, Senha);

        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.PostAsync($"/api/usuarios/{alvo}/desativar")).StatusCode);
        Assert.True((await BuscarAsync(operador)).Ativo);
    }

    [Fact]
    public async Task Desativar_sem_sessao_responde_401()
    {
        var cliente = await ClienteApi.AbrirAsync(factory);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await cliente.PostAsync($"/api/usuarios/{Guid.NewGuid()}/desativar")).StatusCode);
    }

    [Fact]
    public async Task Desativar_usuario_inexistente_responde_404()
    {
        var admin = await AdminAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"/api/usuarios/{Guid.NewGuid()}/desativar")).StatusCode);
    }

    // ---- T3: sessões no servidor ----

    [Fact]
    public async Task Login_grava_a_sessao_no_banco_com_fim_em_12_horas()
    {
        var login = await NovoUsuarioAsync("sessao");
        var cliente = await ClienteApi.AbrirAsync(factory);
        await cliente.EntrarAsync(login, Senha);
        var id = (await BuscarAsync(login)).Id;

        await using var escopo = factory.Services.CreateAsyncScope();
        var sessao = await escopo.ServiceProvider.GetRequiredService<AcessoDbContext>().Sessoes.SingleAsync(s => s.UsuarioId == id);

        Assert.Null(sessao.RevogadaEm);
        Assert.InRange(sessao.ExpiraEm - sessao.CriadaEm, TimeSpan.FromHours(12).Subtract(TimeSpan.FromSeconds(2)),
            TimeSpan.FromHours(12).Add(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task Cookie_guardado_antes_do_logout_e_reenviado_depois_recebe_401()
    {
        var login = await NovoUsuarioAsync("copia");
        var cliente = await ClienteApi.AbrirAsync(factory);
        await cliente.EntrarAsync(login, Senha);
        var copia = cliente.UltimosSetCookie["impa.sessao"].Split(';', 2)[0];

        using var comCopia = CriarClienteSemCookies();
        Assert.Equal(HttpStatusCode.OK, (await ComCookie(comCopia, copia)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await cliente.SairAsync()).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await ComCookie(comCopia, copia)).StatusCode);
    }

    private HttpClient CriarClienteSemCookies() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = false,
    });

    private static Task<HttpResponseMessage> ComCookie(HttpClient http, string cookie)
    {
        var requisicao = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        requisicao.Headers.Add("Cookie", cookie);
        return http.SendAsync(requisicao);
    }
}
