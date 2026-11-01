using System.Net;
using System.Net.Http.Json;
using Impa.Modules.Acesso.Application.Perfis;
using Npgsql;

namespace Impa.Api.IntegrationTests;

/// <summary>T3/T5 — login, logout, sessão atual e proteção CSRF (CA-1, CA-2, CA-3).</summary>
public class AutenticacaoTests(ImpaApiFactory factory) : IClassFixture<ImpaApiFactory>, IAsyncLifetime
{
    private const string MensagemCredencialInvalida = "Usuário ou senha inválidos";

    public async Task InitializeAsync() =>
        await factory.CriarUsuarioAsync("ana", "balcao2026", "Ana", PerfisPadrao.OperadorDeCaixa.Nome);

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record SessaoResposta(string Usuario, string Nome, string Perfil, string[] Permissoes);

    private sealed record ErroResposta(string Mensagem);

    [Fact]
    public async Task Cenario_1_1_operadora_entra_e_ve_so_as_funcoes_do_seu_perfil()
    {
        var cliente = await ClienteApi.AbrirAsync(factory);

        var login = await cliente.EntrarAsync("ana", "balcao2026");
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

        var me = await cliente.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        var sessao = await me.Content.ReadFromJsonAsync<SessaoResposta>();
        Assert.NotNull(sessao);
        Assert.Equal("ana", sessao.Usuario);
        Assert.Equal("Ana", sessao.Nome);
        Assert.Equal(PerfisPadrao.OperadorDeCaixa.Nome, sessao.Perfil);
        Assert.Equal(PerfisPadrao.OperadorDeCaixa.Permissoes.Order(), sessao.Permissoes.Order());
    }

    [Fact]
    public async Task Administrador_recebe_todas_as_permissoes_no_me()
    {
        var cliente = await ClienteApi.AbrirAsync(factory);
        await cliente.EntrarAsync(ImpaApiFactory.AdminUsuario, ImpaApiFactory.AdminSenha);

        var sessao = await (await cliente.GetAsync("/api/auth/me")).Content.ReadFromJsonAsync<SessaoResposta>();

        Assert.Equal(PerfisPadrao.Administrador.Nome, sessao!.Perfil);
        Assert.Equal(PerfisPadrao.Administrador.Permissoes.Order(), sessao.Permissoes.Order());
    }

    [Theory]
    [InlineData("ana", "errada123")]        // cenário 1.2: senha errada
    [InlineData("nao-existe", "errada123")] // usuário inexistente: mesma resposta
    public async Task Cenario_1_2_credencial_invalida_e_recusada_sem_revelar_qual_dado_esta_errado(
        string usuario, string senha)
    {
        var cliente = await ClienteApi.AbrirAsync(factory);

        var login = await cliente.EntrarAsync(usuario, senha);

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.Equal(MensagemCredencialInvalida, (await login.Content.ReadFromJsonAsync<ErroResposta>())!.Mensagem);
        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Login_sem_usuario_ou_senha_e_recusado_por_validacao()
    {
        var cliente = await ClienteApi.AbrirAsync(factory);

        var login = await cliente.EntrarAsync("", "");

        Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
    }

    [Fact]
    public async Task Me_sem_sessao_responde_401_sem_redirecionar()
    {
        var resposta = await factory.CriarCliente().GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        Assert.Null(resposta.Headers.Location);
    }

    [Fact]
    public async Task Logout_encerra_a_sessao()
    {
        var cliente = await ClienteApi.AbrirAsync(factory);
        await cliente.EntrarAsync("ana", "balcao2026");

        var logout = await cliente.SairAsync();

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Cookie_de_sessao_e_HttpOnly_Secure_e_SameSite_Strict()
    {
        var cliente = await ClienteApi.AbrirAsync(factory);
        await cliente.EntrarAsync("ana", "balcao2026");

        var cookie = cliente.UltimosSetCookie["impa.sessao"].ToLowerInvariant();

        Assert.Contains("httponly", cookie);
        Assert.Contains("secure", cookie);
        Assert.Contains("samesite=strict", cookie);
    }

    [Fact]
    public async Task Requisicao_que_altera_estado_sem_token_xsrf_e_recusada()
    {
        var cliente = await ClienteApi.AbrirAsync(factory);

        var login = await cliente.PostAsync("/api/auth/login", new { usuario = "ana", senha = "balcao2026" }, enviarXsrf: false);

        Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Token_xsrf_e_renovado_no_login_e_o_novo_vale_para_o_logout()
    {
        var cliente = await ClienteApi.AbrirAsync(factory);
        var tokenAnonimo = cliente.TokenXsrf;

        await cliente.EntrarAsync("ana", "balcao2026");

        Assert.NotNull(tokenAnonimo);
        Assert.NotEqual(tokenAnonimo, cliente.TokenXsrf);
        Assert.Equal(HttpStatusCode.NoContent, (await cliente.SairAsync()).StatusCode);
    }

    [Fact]
    public async Task Senha_e_guardada_so_como_hash()
    {
        _ = factory.CriarCliente();
        await using var conexao = new NpgsqlConnection(factory.ConnectionString);
        await conexao.OpenAsync();
        await using var comando = new NpgsqlCommand(
            """select "PasswordHash" from acesso.usuarios where "UserName" = 'ana'""", conexao);

        var hash = (string?)await comando.ExecuteScalarAsync();

        Assert.False(string.IsNullOrEmpty(hash));
        Assert.DoesNotContain("balcao2026", hash);
    }
}
