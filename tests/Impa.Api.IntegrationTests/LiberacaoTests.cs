using System.Net;
using System.Net.Http.Json;
using Impa.Modules.Acesso.Application.Liberacoes;
using Impa.Modules.Acesso.Application.Perfis;
using Impa.Modules.Acesso.Contracts;
using Impa.Modules.Acesso.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Impa.Api.IntegrationTests;

/// <summary>Story 1-5 — liberação de operação por administrador (cenário 1.8, RN-1.9).</summary>
public class LiberacaoTests(ImpaApiFactory factory) : IClassFixture<ImpaApiFactory>, IAsyncLifetime
{
    private const string AnaSenha = "balcao2026";

    public async Task InitializeAsync() =>
        await factory.CriarUsuarioAsync("ana", AnaSenha, "Ana", PerfisPadrao.OperadorDeCaixa.Nome);

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record PermissaoRequerida(string? Mensagem, string Codigo, string Permissao, bool Liberavel);

    private async Task<ClienteApi> AnaAsync()
    {
        var cliente = await ClienteApi.AbrirAsync(factory);
        Assert.Equal(HttpStatusCode.NoContent, (await cliente.EntrarAsync("ana", AnaSenha)).StatusCode);
        return cliente;
    }

    // ---- T1: 403 por permissão traz { codigo: "permissao_requerida", permissao, liberavel } ----

    [Fact]
    public async Task Operador_sem_a_permissao_liberavel_recebe_403_liberavel()
    {
        var ana = await AnaAsync();

        var resposta = await ana.PostAsync(EndpointsDeExemplo.CancelarVendaConcluida);

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        var corpo = await resposta.Content.ReadFromJsonAsync<PermissaoRequerida>();
        Assert.Equal("permissao_requerida", corpo!.Codigo);
        Assert.Equal(Permissoes.VendasCancelarConcluida, corpo.Permissao);
        Assert.True(corpo.Liberavel);
    }

    [Fact]
    public async Task Permissao_que_nao_e_liberavel_responde_403_nao_liberavel()
    {
        var ana = await AnaAsync();

        var resposta = await ana.GetAsync("/api/configuracao/empresa");

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        var corpo = await resposta.Content.ReadFromJsonAsync<PermissaoRequerida>();
        Assert.Equal("permissao_requerida", corpo!.Codigo);
        Assert.Equal(Permissoes.ConfiguracaoVer, corpo.Permissao);
        Assert.False(corpo.Liberavel);
        // Sem "mensagem": as telas mantêm o texto próprio do 403 (ex.: "Você não tem permissão para gerenciar usuários").
        Assert.Null(corpo.Mensagem);
    }

    [Fact]
    public async Task Sem_a_permissao_e_sem_terminal_o_403_nao_e_liberavel()
    {
        // Liberar a permissão não resolveria: ainda faltaria o terminal registrado.
        var ana = await AnaAsync();

        var resposta = await ana.PostAsync(EndpointsDeExemplo.CancelarVendaNoTerminal);

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        var corpo = await resposta.Content.ReadFromJsonAsync<PermissaoRequerida>();
        Assert.Equal("permissao_requerida", corpo!.Codigo);
        Assert.Equal(Permissoes.VendasCancelarConcluida, corpo.Permissao);
        Assert.False(corpo.Liberavel);
    }

    [Fact]
    public void Catalogo_marca_as_permissoes_liberaveis()
    {
        Assert.True(Permissoes.EhLiberavel(Permissoes.PdvDescontoAcimaDoLimite));
        Assert.True(Permissoes.EhLiberavel(Permissoes.VendasCancelarConcluida));
        Assert.False(Permissoes.EhLiberavel(Permissoes.LiberarOperacoes));
        Assert.False(Permissoes.EhLiberavel(Permissoes.ConfiguracaoVer));
    }

    // ---- T2: POST /api/acesso/liberacoes ----

    private const string Liberacoes = "/api/acesso/liberacoes";
    private const string Cabecalho = "X-Liberacao";

    private sealed record LiberacaoResposta(string Token, DateTimeOffset ExpiraEm);

    private sealed record Erro(string Mensagem);

    private Task<Guid> CarlaAsync(string login = "carla") =>
        factory.CriarUsuarioAsync(login, "gerencia2026", "Carla", PerfisPadrao.Administrador.Nome);

    private static Task<HttpResponseMessage> PedirLiberacaoAsync(ClienteApi cliente, string usuario = "carla",
        string senha = "gerencia2026", string permissao = Permissoes.VendasCancelarConcluida) =>
        cliente.PostAsync(Liberacoes, new { usuario, senha, permissao });

    private static async Task<string> LiberarAsync(ClienteApi cliente, string permissao = Permissoes.VendasCancelarConcluida)
    {
        var resposta = await PedirLiberacaoAsync(cliente, permissao: permissao);
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        return (await resposta.Content.ReadFromJsonAsync<LiberacaoResposta>())!.Token;
    }

    private static Task<HttpResponseMessage> CancelarComTokenAsync(ClienteApi cliente, string token) =>
        cliente.EnviarAsync(HttpMethod.Post, EndpointsDeExemplo.CancelarVendaConcluida, null, cabecalhos: new() { [Cabecalho] = token });

    [Fact]
    public async Task Administradora_libera_e_recebe_token_valido_por_2_minutos()
    {
        await CarlaAsync();
        var ana = await AnaAsync();

        var resposta = await PedirLiberacaoAsync(ana);

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var corpo = (await resposta.Content.ReadFromJsonAsync<LiberacaoResposta>())!;
        Assert.False(string.IsNullOrEmpty(corpo.Token));
        Assert.Equal(factory.Clock.GetUtcNow().AddMinutes(2), corpo.ExpiraEm, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Senha_errada_e_recusada_com_403_sem_derrubar_a_sessao_do_operador()
    {
        await CarlaAsync();
        var ana = await AnaAsync();

        var resposta = await PedirLiberacaoAsync(ana, senha: "errada-123");

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Equal("Usuário ou senha inválidos", (await resposta.Content.ReadFromJsonAsync<Erro>())!.Mensagem);
        Assert.Equal(HttpStatusCode.OK, (await ana.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Usuario_inexistente_tem_a_mesma_resposta_da_senha_errada()
    {
        var ana = await AnaAsync();

        var resposta = await PedirLiberacaoAsync(ana, usuario: "ninguem");

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Equal("Usuário ou senha inválidos", (await resposta.Content.ReadFromJsonAsync<Erro>())!.Mensagem);
    }

    [Fact]
    public async Task Senha_errada_na_liberacao_conta_para_o_bloqueio_do_liberador()
    {
        await CarlaAsync("carla-bloqueio");
        var ana = await AnaAsync();

        for (var i = 0; i < 5; i++)
            await PedirLiberacaoAsync(ana, usuario: "carla-bloqueio", senha: "errada-123");

        // RN-1.2: bloqueada, nem a senha certa libera — nem entra.
        var liberacao = await PedirLiberacaoAsync(ana, usuario: "carla-bloqueio");
        Assert.Equal(HttpStatusCode.Forbidden, liberacao.StatusCode);
        Assert.Contains("bloqueado", (await liberacao.Content.ReadFromJsonAsync<Erro>())!.Mensagem);
        var login = await (await ClienteApi.AbrirAsync(factory)).EntrarAsync("carla-bloqueio", "gerencia2026");
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.Contains("bloqueado", await login.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Quem_nao_tem_liberar_operacoes_nao_libera()
    {
        await factory.CriarUsuarioAsync("bia", "balcao2027", "Bia", PerfisPadrao.OperadorDeCaixa.Nome);
        var ana = await AnaAsync();

        var resposta = await PedirLiberacaoAsync(ana, usuario: "bia", senha: "balcao2027");

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Equal("Este usuário não pode liberar esta operação", (await resposta.Content.ReadFromJsonAsync<Erro>())!.Mensagem);
    }

    [Fact]
    public async Task Quem_tem_liberar_operacoes_mas_nao_a_permissao_pedida_nao_libera()
    {
        var admin = await ClienteApi.AbrirAsync(factory);
        await admin.EntrarAsync(ImpaApiFactory.AdminUsuario, ImpaApiFactory.AdminSenha);
        await admin.PostAsync("/api/perfis", new { nome = "Supervisor de desconto", permissoes = new[] { Permissoes.LiberarOperacoes, Permissoes.PdvDescontoAcimaDoLimite } });
        await factory.CriarUsuarioAsync("dora", "supervisao2026", "Dora", "Supervisor de desconto");
        var ana = await AnaAsync();

        var cancelar = await PedirLiberacaoAsync(ana, usuario: "dora", senha: "supervisao2026");
        var desconto = await PedirLiberacaoAsync(ana, usuario: "dora", senha: "supervisao2026", permissao: Permissoes.PdvDescontoAcimaDoLimite);

        Assert.Equal(HttpStatusCode.Forbidden, cancelar.StatusCode);
        Assert.Equal(HttpStatusCode.Created, desconto.StatusCode);
    }

    [Fact]
    public async Task Permissao_que_nao_e_liberavel_e_recusada_na_validacao()
    {
        await CarlaAsync();
        var ana = await AnaAsync();

        var resposta = await PedirLiberacaoAsync(ana, permissao: Permissoes.UsuariosGerenciar);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Sem_sessao_nao_ha_liberacao()
    {
        var anonimo = await ClienteApi.AbrirAsync(factory);

        Assert.Equal(HttpStatusCode.Unauthorized, (await PedirLiberacaoAsync(anonimo)).StatusCode);
    }

    // ---- T3/T4/T6: X-Liberacao na política, uso único, registro ----

    [Fact]
    public async Task Com_o_token_a_operacao_e_executada_e_registrada_com_operador_e_liberador()
    {
        var carlaId = await CarlaAsync();
        var anaId = await factory.IdDoUsuarioAsync("ana");
        var ana = await AnaAsync();
        var token = await LiberarAsync(ana);

        var resposta = await CancelarComTokenAsync(ana, token);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal(new OperacaoRegistrada(anaId, carlaId), await resposta.Content.ReadFromJsonAsync<OperacaoRegistrada>());

        await using var escopo = factory.Services.CreateAsyncScope();
        var registro = await escopo.ServiceProvider.GetRequiredService<AcessoDbContext>().Liberacoes
            .AsNoTracking().SingleAsync(l => l.TokenHash == TokenDeLiberacao.Hash(token));
        Assert.Equal(anaId, registro.OperadorId);
        Assert.Equal(carlaId, registro.LiberadorId);
        Assert.Equal(Permissoes.VendasCancelarConcluida, registro.Permissao);
        Assert.Equal($"POST {EndpointsDeExemplo.CancelarVendaConcluida}", registro.Operacao);
        Assert.NotNull(registro.UsadaEm);
    }

    [Fact]
    public async Task Token_reutilizado_e_recusado_e_a_proxima_operacao_pede_nova_liberacao()
    {
        await CarlaAsync();
        var ana = await AnaAsync();
        var token = await LiberarAsync(ana);
        Assert.Equal(HttpStatusCode.OK, (await CancelarComTokenAsync(ana, token)).StatusCode);

        var segunda = await CancelarComTokenAsync(ana, token);

        Assert.Equal(HttpStatusCode.Forbidden, segunda.StatusCode);
        Assert.True((await segunda.Content.ReadFromJsonAsync<PermissaoRequerida>())!.Liberavel);
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.PostAsync(EndpointsDeExemplo.CancelarVendaConcluida)).StatusCode);
    }

    [Fact]
    public async Task Token_de_outra_permissao_e_recusado()
    {
        await CarlaAsync();
        var ana = await AnaAsync();
        var token = await LiberarAsync(ana, Permissoes.PdvDescontoAcimaDoLimite);

        Assert.Equal(HttpStatusCode.Forbidden, (await CancelarComTokenAsync(ana, token)).StatusCode);
    }

    [Fact]
    public async Task Token_vencido_e_recusado()
    {
        await CarlaAsync();
        var ana = await AnaAsync();
        var token = await LiberarAsync(ana);

        factory.Clock.Advance(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(1));

        Assert.Equal(HttpStatusCode.Forbidden, (await CancelarComTokenAsync(ana, token)).StatusCode);
    }

    [Fact]
    public async Task Token_de_outra_sessao_e_recusado()
    {
        await CarlaAsync();
        var ana = await AnaAsync();
        var token = await LiberarAsync(ana);
        var outraSessaoDaAna = await AnaAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await CancelarComTokenAsync(outraSessaoDaAna, token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await CancelarComTokenAsync(ana, token)).StatusCode);
    }

    [Fact]
    public async Task Token_invalido_no_cabecalho_e_ignorado()
    {
        var ana = await AnaAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await CancelarComTokenAsync(ana, "nao-e-um-token")).StatusCode);
    }

    [Fact]
    public async Task Liberador_que_perdeu_a_permissao_antes_do_uso_nao_vale_mais()
    {
        var admin = await ClienteApi.AbrirAsync(factory);
        await admin.EntrarAsync(ImpaApiFactory.AdminUsuario, ImpaApiFactory.AdminSenha);
        await admin.PostAsync("/api/perfis", new { nome = "Supervisor temporario", permissoes = new[] { Permissoes.LiberarOperacoes, Permissoes.VendasCancelarConcluida } });
        var evaId = await factory.CriarUsuarioAsync("eva", "supervisao2027", "Eva", "Supervisor temporario");
        var ana = await AnaAsync();
        var resposta = await PedirLiberacaoAsync(ana, usuario: "eva", senha: "supervisao2027");
        var token = (await resposta.Content.ReadFromJsonAsync<LiberacaoResposta>())!.Token;

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/usuarios/{evaId}/desativar")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await CancelarComTokenAsync(ana, token)).StatusCode);
    }

    [Fact]
    public async Task Operador_que_tem_a_permissao_opera_sem_liberador()
    {
        var admin = await ClienteApi.AbrirAsync(factory);
        await admin.EntrarAsync(ImpaApiFactory.AdminUsuario, ImpaApiFactory.AdminSenha);

        var resposta = await admin.PostAsync(EndpointsDeExemplo.CancelarVendaConcluida);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Null((await resposta.Content.ReadFromJsonAsync<OperacaoRegistrada>())!.Liberador);
    }
}
