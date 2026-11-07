using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Impa.Modules.Acesso.Application.Perfis;
using Impa.Modules.Acesso.Contracts;

namespace Impa.Api.IntegrationTests;

/// <summary>Story 1-3, T8 — cenário 1.5 (CA-1), perfis e permissões (CA-3, CA-4, CA-5).</summary>
public class PerfisTests(ImpaApiFactory factory) : IClassFixture<ImpaApiFactory>, IAsyncLifetime
{
    private const string SenhaAna = "balcao2026";

    private sealed record ErroResposta(string Mensagem);

    public async Task InitializeAsync() =>
        await factory.CriarUsuarioAsync("ana", SenhaAna, "Ana", PerfisPadrao.OperadorDeCaixa.Nome);

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<ClienteApi> EntrarAsync(string usuario, string senha)
    {
        var cliente = await ClienteApi.AbrirAsync(factory);
        Assert.Equal(HttpStatusCode.NoContent, (await cliente.EntrarAsync(usuario, senha)).StatusCode);
        return cliente;
    }

    private Task<ClienteApi> AdminAsync() => EntrarAsync(ImpaApiFactory.AdminUsuario, ImpaApiFactory.AdminSenha);

    private Task<ClienteApi> PerfilAsync(string perfil) => perfil switch
    {
        "Administrador" => AdminAsync(),
        "Operador de caixa" => EntrarAsync("ana", SenhaAna),
        _ => throw new ArgumentOutOfRangeException(nameof(perfil)),
    };

    private static async Task<HttpResponseMessage> TentarAsync(ClienteApi cliente, string acao) => acao switch
    {
        "vender no PDV" => await cliente.PostAsync(EndpointsDeExemplo.Pdv),
        "ver o custo de um item" => await cliente.GetAsync(EndpointsDeExemplo.Item),
        "abrir as contas a pagar" => await cliente.GetAsync(EndpointsDeExemplo.ContasAPagar),
        "alterar a configuração fiscal" => await cliente.PutAsync(EndpointsDeExemplo.ConfiguracaoFiscal),
        "importar planilhas" => await cliente.PostAsync(EndpointsDeExemplo.Importacao),
        _ => throw new ArgumentOutOfRangeException(nameof(acao)),
    };

    // ---- Cenário 1.5 (esquema): uma linha do exemplo por caso ----

    [Theory]
    [InlineData("Operador de caixa", "vender no PDV", "permitido")]
    [InlineData("Operador de caixa", "ver o custo de um item", "o custo não é exibido")]
    [InlineData("Operador de caixa", "abrir as contas a pagar", "acesso negado")]
    [InlineData("Operador de caixa", "alterar a configuração fiscal", "acesso negado")]
    [InlineData("Operador de caixa", "importar planilhas", "acesso negado")]
    [InlineData("Administrador", "ver o custo de um item", "permitido")]
    [InlineData("Administrador", "alterar a configuração fiscal", "permitido")]
    public async Task Cenario_1_5_restringir_funcoes_conforme_as_permissoes_do_perfil(string perfil, string acao, string resultado)
    {
        var cliente = await PerfilAsync(perfil);

        var resposta = await TentarAsync(cliente, acao);

        switch (resultado)
        {
            case "acesso negado":
                Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
                break;
            case "o custo não é exibido":
                Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
                var semCusto = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
                Assert.False(semCusto.TryGetProperty("custo", out _), "o campo custo não pode ir no JSON");
                Assert.True(semCusto.TryGetProperty("preco", out _));
                break;
            case "permitido":
                Assert.True(resposta.IsSuccessStatusCode, $"esperado 2xx, veio {(int)resposta.StatusCode}");
                if (acao == "ver o custo de um item")
                {
                    var comCusto = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
                    Assert.Equal(18.40m, comCusto.GetProperty("custo").GetDecimal());
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(resultado));
        }
    }

    // ---- CA-3, CA-4: perfis e permissões ----

    [Fact]
    public async Task Lista_traz_o_Administrador_protegido_com_todas_as_permissoes_e_o_Operador_com_as_da_RN_1_5()
    {
        var perfis = await (await (await AdminAsync()).GetAsync("/api/perfis")).Content.ReadFromJsonAsync<List<PerfilDto>>();

        var admin = Assert.Single(perfis!, p => p.Nome == "Administrador");
        Assert.True(admin.Protegido);
        Assert.Equal(Permissoes.Todas.Order(), admin.Permissoes);
        Assert.Equal("Administrador", perfis![0].Nome);

        var operador = Assert.Single(perfis, p => p.Nome == "Operador de caixa");
        Assert.False(operador.Protegido);
        Assert.Equal(PerfisPadrao.OperadorDeCaixa.Permissoes.Order(), operador.Permissoes);
    }

    [Fact]
    public async Task Catalogo_de_permissoes_vem_agrupado()
    {
        var catalogo = await (await (await AdminAsync()).GetAsync("/api/perfis/permissoes")).Content
            .ReadFromJsonAsync<List<PermissaoInfo>>();

        Assert.Equal(Permissoes.Todas, catalogo!.Select(p => p.Codigo));
        Assert.Contains(catalogo!, p => p is { Codigo: "catalogo.custo.ver", Grupo: "Catálogo" });
    }

    [Fact]
    public async Task Operador_de_caixa_nao_gerencia_perfis()
    {
        var ana = await EntrarAsync("ana", SenhaAna);

        Assert.Equal(HttpStatusCode.Forbidden, (await ana.GetAsync("/api/perfis")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.PostAsync("/api/perfis", new { nome = "Meu", permissoes = Array.Empty<string>() })).StatusCode);
    }

    [Fact]
    public async Task Criar_alterar_e_excluir_um_perfil()
    {
        var admin = await AdminAsync();

        var criado = await admin.PostAsync("/api/perfis", new { nome = "Estoquista", permissoes = new[] { Permissoes.CatalogoVer, Permissoes.CatalogoEditar } });
        Assert.Equal(HttpStatusCode.Created, criado.StatusCode);
        var perfil = (await criado.Content.ReadFromJsonAsync<PerfilDto>())!;
        Assert.Equal([Permissoes.CatalogoEditar, Permissoes.CatalogoVer], perfil.Permissoes);

        var alterado = await admin.PutAsync($"/api/perfis/{perfil.Id}", new { nome = "Estoque", permissoes = new[] { Permissoes.CatalogoVer } });
        Assert.Equal(HttpStatusCode.OK, alterado.StatusCode);
        var depois = (await alterado.Content.ReadFromJsonAsync<PerfilDto>())!;
        Assert.Equal("Estoque", depois.Nome);
        Assert.Equal([Permissoes.CatalogoVer], depois.Permissoes);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/perfis/{perfil.Id}")).StatusCode);
        var perfis = await (await admin.GetAsync("/api/perfis")).Content.ReadFromJsonAsync<List<PerfilDto>>();
        Assert.DoesNotContain(perfis!, p => p.Id == perfil.Id);
    }

    [Fact]
    public async Task Nome_repetido_responde_409_e_permissao_desconhecida_400()
    {
        var admin = await AdminAsync();

        var repetido = await admin.PostAsync("/api/perfis", new { nome = "operador de caixa", permissoes = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
        Assert.Equal(MensagensPerfis.NomeEmUso, (await repetido.Content.ReadFromJsonAsync<ErroResposta>())!.Mensagem);

        var desconhecida = await admin.PostAsync("/api/perfis", new { nome = "Novo", permissoes = new[] { "tudo.liberado" } });
        Assert.Equal(HttpStatusCode.BadRequest, desconhecida.StatusCode);
    }

    [Fact]
    public async Task Perfil_com_usuarios_nao_pode_ser_excluido()
    {
        var admin = await AdminAsync();
        var operador = (await (await admin.GetAsync("/api/perfis")).Content.ReadFromJsonAsync<List<PerfilDto>>())!
            .Single(p => p.Nome == "Operador de caixa");

        var resposta = await admin.DeleteAsync($"/api/perfis/{operador.Id}");

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal(MensagensPerfis.PerfilComUsuarios, (await resposta.Content.ReadFromJsonAsync<ErroResposta>())!.Mensagem);
    }

    // ---- CA-5: Administrador protegido ----

    [Fact]
    public async Task Perfil_Administrador_nao_pode_ser_alterado_nem_excluido()
    {
        var admin = await AdminAsync();
        var perfil = (await (await admin.GetAsync("/api/perfis")).Content.ReadFromJsonAsync<List<PerfilDto>>())!
            .Single(p => p.Protegido);

        var alterar = await admin.PutAsync($"/api/perfis/{perfil.Id}", new { nome = "Administrador", permissoes = new[] { Permissoes.PdvVender } });
        Assert.Equal(HttpStatusCode.Conflict, alterar.StatusCode);
        Assert.Equal(MensagensPerfis.AdministradorNaoAlteravel, (await alterar.Content.ReadFromJsonAsync<ErroResposta>())!.Mensagem);

        var excluir = await admin.DeleteAsync($"/api/perfis/{perfil.Id}");
        Assert.Equal(HttpStatusCode.Conflict, excluir.StatusCode);
        Assert.Equal(MensagensPerfis.AdministradorNaoExcluivel, (await excluir.Content.ReadFromJsonAsync<ErroResposta>())!.Mensagem);

        var depois = (await (await admin.GetAsync("/api/perfis")).Content.ReadFromJsonAsync<List<PerfilDto>>())!.Single(p => p.Protegido);
        Assert.Equal(Permissoes.Todas.Order(), depois.Permissoes);
    }

    // ---- T5: permissões vêm do banco, valem na próxima requisição ----

    [Fact]
    public async Task Alterar_as_permissoes_do_perfil_vale_na_proxima_requisicao_sem_novo_login_e_aparece_no_me()
    {
        var admin = await AdminAsync();
        var criado = await admin.PostAsync("/api/perfis", new { nome = "Financeiro", permissoes = new[] { Permissoes.CatalogoVer } });
        var perfil = (await criado.Content.ReadFromJsonAsync<PerfilDto>())!;
        await factory.CriarUsuarioAsync("fabio", "financeiro-2026", "Fábio", perfil.Nome);
        var fabio = await EntrarAsync("fabio", "financeiro-2026");

        Assert.Equal(HttpStatusCode.Forbidden, (await fabio.GetAsync(EndpointsDeExemplo.ContasAPagar)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsync($"/api/perfis/{perfil.Id}",
            new { nome = perfil.Nome, permissoes = new[] { Permissoes.CatalogoVer, Permissoes.FinanceiroVer } })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await fabio.GetAsync(EndpointsDeExemplo.ContasAPagar)).StatusCode);
        var me = JsonDocument.Parse(await (await fabio.GetAsync("/api/auth/me")).Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Financeiro", me.GetProperty("perfil").GetString());
        Assert.Equal([Permissoes.CatalogoVer, Permissoes.FinanceiroVer],
            me.GetProperty("permissoes").EnumerateArray().Select(p => p.GetString()!).ToArray());

        // Tirar a permissão também vale na hora.
        await admin.PutAsync($"/api/perfis/{perfil.Id}", new { nome = perfil.Nome, permissoes = new[] { Permissoes.CatalogoVer } });
        Assert.Equal(HttpStatusCode.Forbidden, (await fabio.GetAsync(EndpointsDeExemplo.ContasAPagar)).StatusCode);
    }
}
