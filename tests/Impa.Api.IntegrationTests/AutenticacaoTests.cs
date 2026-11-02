using System.Net;
using System.Net.Http.Json;
using Impa.Modules.Acesso.Application.Autenticacao;
using Impa.Modules.Acesso.Application.Perfis;
using Impa.Modules.Acesso.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
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
    public async Task Cenario_1_2_senha_errada_e_usuario_inexistente_recebem_respostas_identicas()
    {
        var senhaErrada = await (await ClienteApi.AbrirAsync(factory)).EntrarAsync("ana", "errada123");
        var inexistente = await (await ClienteApi.AbrirAsync(factory)).EntrarAsync("nao-existe", "errada123");

        Assert.Equal(senhaErrada.StatusCode, inexistente.StatusCode);
        Assert.Equal(await senhaErrada.Content.ReadAsStringAsync(), await inexistente.Content.ReadAsStringAsync());
        Assert.Equal(Cabecalhos(senhaErrada), Cabecalhos(inexistente));
    }

    [Fact]
    public async Task Usuario_inexistente_gasta_uma_conferencia_de_senha_como_a_senha_errada()
    {
        factory.Hasher.Zerar();
        await (await ClienteApi.AbrirAsync(factory)).EntrarAsync("ana", "errada123");
        var conferenciasSenhaErrada = factory.Hasher.Conferencias;

        factory.Hasher.Zerar();
        await (await ClienteApi.AbrirAsync(factory)).EntrarAsync("nao-existe", "errada123");

        Assert.Equal(1, conferenciasSenhaErrada);
        Assert.Equal(conferenciasSenhaErrada, factory.Hasher.Conferencias);
    }

    [Fact]
    public async Task Hash_ficticio_ja_esta_pronto_antes_do_primeiro_login_inexistente()
    {
        var cliente = await ClienteApi.AbrirAsync(factory); // Host no ar
        factory.Hasher.Zerar();

        await cliente.EntrarAsync("nao-existe-ainda", "errada123");

        // Só a conferência: nenhum PBKDF2 a mais calculando o hash fictício nesta requisição (R15).
        Assert.Equal(0, factory.Hasher.Hashes);
        Assert.Equal(1, factory.Hasher.Conferencias);
    }

    [Fact]
    public async Task Conferencia_de_senha_e_primitiva_pura_mesmo_com_a_conta_bloqueada()
    {
        await factory.CriarUsuarioAsync("bloqueada", "senha-certa-123", "Bloqueada", PerfisPadrao.OperadorDeCaixa.Nome);
        await using var escopo = factory.Services.CreateAsyncScope();
        var usuarios = escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        var usuario = (await usuarios.FindByNameAsync("bloqueada"))!;
        await usuarios.SetLockoutEnabledAsync(usuario, true);
        await usuarios.SetLockoutEndDateAsync(usuario, DateTimeOffset.UtcNow.AddYears(1));
        var sessao = escopo.ServiceProvider.GetRequiredService<ISessaoService>();

        factory.Hasher.Zerar();
        var confere = await sessao.SenhaConfereAsync(usuario.Id, "senha-certa-123", CancellationToken.None);

        // Bloqueio é decisão do handler (story 1.2); a primitiva sempre calcula o hash (R16).
        Assert.True(confere);
        Assert.Equal(1, factory.Hasher.Conferencias);
    }

    [Fact]
    public async Task Espaco_no_fim_do_usuario_e_ignorado()
    {
        var cliente = await ClienteApi.AbrirAsync(factory);

        var login = await cliente.EntrarAsync("ana ", "balcao2026");

        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
    }

    [Fact]
    public async Task Usuario_so_com_espacos_e_recusado_por_validacao()
    {
        var cliente = await ClienteApi.AbrirAsync(factory);

        var login = await cliente.EntrarAsync("   ", "balcao2026");

        Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
    }

    // Cabeçalhos que poderiam diferenciar as respostas (Date muda a cada requisição).
    private static string Cabecalhos(HttpResponseMessage resposta) => string.Join('\n',
        resposta.Headers.Concat(resposta.Content.Headers)
            .Where(h => h.Key != "Date")
            .Select(h => $"{h.Key}: {(h.Key == "Set-Cookie" ? string.Join(",", h.Value.Select(v => v.Split('=')[0])) : string.Join(",", h.Value))}")
            .Order());

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
    public async Task Login_por_http_sem_tls_e_recusado_sem_erro_500()
    {
        var resposta = await factory.CriarCliente(https: false).PostAsJsonAsync(
            "/api/auth/login", new { usuario = "ana", senha = "balcao2026" });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
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

        // Formato V3 do Identity (marcador 0x01, PBKDF2 com sal), e não uma codificação reversível.
        var bytes = Convert.FromBase64String(hash);
        Assert.Equal(0x01, bytes[0]);
        Assert.DoesNotContain("balcao2026", System.Text.Encoding.UTF8.GetString(bytes));

        var hasher = new PasswordHasher<Usuario>();
        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(new Usuario(), hash, "balcao2026"));
        Assert.Equal(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(new Usuario(), hash, "errada123"));
    }
}
