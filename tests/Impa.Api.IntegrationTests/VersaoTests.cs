using System.Net;
using System.Net.Http.Json;

namespace Impa.Api.IntegrationTests;

/// <summary>Versão da API exibida no rodapé do sistema.</summary>
public class VersaoTests(ImpaApiFactory factory) : IClassFixture<ImpaApiFactory>
{
    private sealed record VersaoResposta(string Versao, string Commit);

    [Fact]
    public async Task Versao_sem_sessao_responde_401()
    {
        var resposta = await factory.CriarCliente().GetAsync("/api/versao");

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Versao_com_sessao_devolve_a_versao_semantica_e_o_commit_curto()
    {
        var cliente = await ClienteApi.AbrirAsync(factory);
        await cliente.EntrarAsync(ImpaApiFactory.AdminUsuario, ImpaApiFactory.AdminSenha);

        var resposta = await cliente.GetAsync("/api/versao");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var versao = (await resposta.Content.ReadFromJsonAsync<VersaoResposta>())!;
        Assert.Matches(@"^\d+\.\d+\.\d+$", versao.Versao);
        // Commit curto do git (o SDK grava o hash no InformationalVersion) ou "dev" sem repositório.
        Assert.Matches(@"^([0-9a-f]{7}|dev)$", versao.Commit);
    }

    [Theory]
    [InlineData("0.1.0+1F55D46C5BFAFB35290C758C20C5C6996100C5D", "0.1.0", "1f55d46")]
    [InlineData("0.1.0+1f55d46", "0.1.0", "1f55d46")]
    [InlineData("0.1.0", "0.1.0", "dev")]
    [InlineData("0.1.0+", "0.1.0", "dev")]
    public void Versao_e_commit_sao_lidos_do_informational_version(string texto, string versao, string commit)
    {
        Assert.Equal(new Impa.Host.Endpoints.VersaoDto(versao, commit), Impa.Host.Endpoints.VersaoDto.DoTexto(texto));
    }
}
