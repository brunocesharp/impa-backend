using System.Net;
using System.Net.Http.Json;
using Impa.Modules.Acesso.Application.Perfis;

namespace Impa.Api.IntegrationTests;

/// <summary>T4/T5 — permissões verificadas no servidor, negar por padrão (CA-1, CA-5, RN-1.10).</summary>
public class AutorizacaoTests(ImpaApiFactory factory) : IClassFixture<ImpaApiFactory>, IAsyncLifetime
{
    private const string Empresa = "/api/configuracao/empresa";

    public async Task InitializeAsync() =>
        await factory.CriarUsuarioAsync("ana", "balcao2026", "Ana", PerfisPadrao.OperadorDeCaixa.Nome);

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record EmpresaResposta(string Nome);

    [Fact]
    public async Task Sem_sessao_o_endpoint_protegido_responde_401()
    {
        var resposta = await factory.CriarCliente().GetAsync(Empresa);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Operador_de_caixa_sem_configuracao_ver_recebe_403()
    {
        var cliente = await ClienteApi.AbrirAsync(factory);
        await cliente.EntrarAsync("ana", "balcao2026");

        var resposta = await cliente.GetAsync(Empresa);

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Null(resposta.Headers.Location);
    }

    [Fact]
    public async Task Administrador_com_configuracao_ver_recebe_o_nome_da_empresa()
    {
        var cliente = await ClienteApi.AbrirAsync(factory);
        await cliente.EntrarAsync(ImpaApiFactory.AdminUsuario, ImpaApiFactory.AdminSenha);

        var resposta = await cliente.GetAsync(Empresa);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal(ImpaApiFactory.NomeEmpresa, (await resposta.Content.ReadFromJsonAsync<EmpresaResposta>())!.Nome);
    }

    [Fact]
    public async Task Rota_da_api_sem_regra_propria_exige_autenticacao()
    {
        // Negar por padrão: a política de fallback cobre qualquer rota que não declare a própria.
        var resposta = await factory.CriarCliente().GetAsync("/api/rota-que-nao-existe");

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Health_e_login_continuam_abertos_sem_sessao()
    {
        var cliente = await ClienteApi.AbrirAsync(factory);

        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/api/health")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await cliente.EntrarAsync("ana", "balcao2026")).StatusCode);
    }
}
