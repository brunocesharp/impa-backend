using System.Net;
using System.Net.Http.Json;
using Impa.Modules.Acesso.Infrastructure;
using Microsoft.Extensions.Hosting.Internal;

namespace Impa.Api.IntegrationTests;

/// <summary>Limites baixos e iguais no login e na recuperação de senha, para mostrar que os contadores são separados.</summary>
public sealed class LimiteDeRecuperacaoFactory : ImpaApiFactory
{
    protected override int TentativasDeLogin => 3;

    protected override int TentativasDeRecuperacao => 3;
}

/// <summary>Story 1-8, R5 da revisão 1 — limite por IP próprio da recuperação de senha, com mensagem neutra.</summary>
public class LimiteDeRecuperacaoTests(LimiteDeRecuperacaoFactory factory) : IClassFixture<LimiteDeRecuperacaoFactory>
{
    private const string MensagemMuitosPedidos = "Muitas tentativas. Aguarde um minuto e tente novamente";

    private sealed record ErroResposta(string Mensagem);

    private async Task<ClienteApi> ClienteDoIpAsync(string ip)
    {
        var http = factory.CriarCliente();
        http.DefaultRequestHeaders.Add("X-Forwarded-For", ip);
        var cliente = new ClienteApi(http);
        await cliente.GetAsync("/api/auth/me");
        return cliente;
    }

    [Fact]
    public async Task Pedidos_acima_do_limite_recebem_429_neutro_e_nao_gastam_as_tentativas_de_login()
    {
        var cliente = await ClienteDoIpAsync("198.51.100.81");
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.NoContent,
                (await cliente.PostAsync("/api/auth/esqueci-senha", new { usuarioOuEmail = $"ninguem-{i}" })).StatusCode);

        var recusado = await cliente.PostAsync("/api/auth/esqueci-senha", new { usuarioOuEmail = "ninguem" });
        Assert.Equal(HttpStatusCode.TooManyRequests, recusado.StatusCode);
        Assert.NotNull(recusado.Headers.RetryAfter);
        Assert.Equal(MensagemMuitosPedidos, (await recusado.Content.ReadFromJsonAsync<ErroResposta>())!.Mensagem);

        // redefinir-senha divide o contador da recuperação
        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await cliente.PostAsync("/api/auth/redefinir-senha", new { token = "x", novaSenha = "novasenha26" })).StatusCode);

        // o login do mesmo IP continua com as 3 tentativas dele
        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.EntrarAsync("ninguem", "errada-123")).StatusCode);
    }

    [Fact]
    public async Task Login_acima_do_limite_continua_com_a_mensagem_do_login()
    {
        var cliente = await ClienteDoIpAsync("198.51.100.82");
        for (var i = 0; i < 3; i++)
            await cliente.EntrarAsync("ninguem", "errada-123");

        var recusado = await cliente.EntrarAsync("ninguem", "errada-123");

        Assert.Equal(HttpStatusCode.TooManyRequests, recusado.StatusCode);
        Assert.Equal("Muitas tentativas de entrar. Aguarde um minuto e tente novamente",
            (await recusado.Content.ReadFromJsonAsync<ErroResposta>())!.Mensagem);
        // e a recuperação do mesmo IP ainda passa
        Assert.Equal(HttpStatusCode.NoContent,
            (await cliente.PostAsync("/api/auth/esqueci-senha", new { usuarioOuEmail = "ninguem" })).StatusCode);
    }
}

/// <summary>Story 1-8, R11 da revisão 1 — o endereço do link exige https fora de Development/Testing.</summary>
public class UrlDaAplicacaoTests
{
    private static HostingEnvironment Ambiente(string nome) => new() { EnvironmentName = nome };

    [Theory]
    [InlineData("https://impa.exemplo.com.br", "Production", true)]
    [InlineData("https://impa.exemplo.com.br", "Homolog", true)]
    [InlineData("http://impa.exemplo.com.br", "Production", false)]
    [InlineData("http://impa.exemplo.com.br", "Homolog", false)]
    [InlineData("http://localhost:4200", "Development", true)]
    [InlineData("http://impa.test", "Testing", true)]
    [InlineData("ftp://impa.exemplo.com.br", "Development", false)]
    [InlineData("impa.exemplo.com.br", "Production", false)]
    [InlineData("", "Development", false)]
    public void Valida_o_esquema_conforme_o_ambiente(string url, string ambiente, bool valida)
    {
        Assert.Equal(valida, DependencyInjection.UrlDaAplicacaoValida(url, Ambiente(ambiente)));
    }
}
