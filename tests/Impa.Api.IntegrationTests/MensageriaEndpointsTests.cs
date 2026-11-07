using System.Net;
using Impa.Modules.Acesso.Application.Perfis;

namespace Impa.Api.IntegrationTests;

/// <summary>Story 1-7, T5 — endpoint interno de reprocessamento de evento com falha (exige configuracao.ver).</summary>
public class MensageriaEndpointsTests(ImpaApiFactory factory) : IClassFixture<ImpaApiFactory>, IAsyncLifetime
{
    private static string Reprocessar(Guid id) => $"/api/mensageria/eventos/{id}/reprocessar";

    public async Task InitializeAsync() =>
        await factory.CriarUsuarioAsync("ana", "balcao2026", "Ana", PerfisPadrao.OperadorDeCaixa.Nome);

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Sem_sessao_responde_401()
    {
        var cliente = await ClienteApi.AbrirAsync(factory);

        var resposta = await cliente.PostAsync(Reprocessar(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Sem_configuracao_ver_responde_403()
    {
        var cliente = await ClienteApi.AbrirAsync(factory);
        await cliente.EntrarAsync("ana", "balcao2026");

        var resposta = await cliente.PostAsync(Reprocessar(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }

    [Fact]
    public async Task Evento_inexistente_responde_404()
    {
        var cliente = await ClienteApi.AbrirAsync(factory);
        await cliente.EntrarAsync(ImpaApiFactory.AdminUsuario, ImpaApiFactory.AdminSenha);

        var resposta = await cliente.PostAsync(Reprocessar(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }
}
