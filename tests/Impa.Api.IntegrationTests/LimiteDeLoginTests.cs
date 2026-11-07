using System.Net;
using System.Net.Http.Json;
using Impa.Modules.Acesso.Application.Perfis;
using Impa.Modules.Acesso.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Impa.Api.IntegrationTests;

/// <summary>
/// Limite de 10 tentativas por IP numa janela curta: o limitador usa o relógio do sistema
/// (não o <see cref="TimeProvider"/> injetado), então "passada a janela" espera de verdade.
/// </summary>
public sealed class LimiteDeLoginFactory : ImpaApiFactory
{
    protected override int TentativasDeLogin => 10;

    public override int JanelaDeLoginEmSegundos => 6;
}

/// <summary>Story 1-10, T2 — limite de tentativas no login por origem (CA-1, CA-2, CA-3).</summary>
public class LimiteDeLoginTests(LimiteDeLoginFactory factory) : IClassFixture<LimiteDeLoginFactory>
{
    private const string Senha = "senha-certa-123";
    private const string MensagemCredencialInvalida = "Usuário ou senha inválidos";
    private const string MensagemMuitasTentativas = "Muitas tentativas de entrar. Aguarde um minuto e tente novamente";

    private sealed record ErroResposta(string Mensagem, string? Codigo);

    /// <summary>Cliente "atrás do Caddy" com um IP próprio (X-Forwarded-For), para cada teste ter seu limite.</summary>
    private async Task<ClienteApi> ClienteDoIpAsync(string ip)
    {
        var http = factory.CriarCliente();
        http.DefaultRequestHeaders.Add("X-Forwarded-For", ip);
        var cliente = new ClienteApi(http);
        await cliente.GetAsync("/api/auth/me");
        return cliente;
    }

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

    /// <summary>Gasta as 10 tentativas da janela com um usuário inexistente, conferindo que respondem como sempre.</summary>
    private static async Task GastarLimiteAsync(ClienteApi cliente)
    {
        for (var i = 0; i < 10; i++)
        {
            var resposta = await cliente.EntrarAsync($"ninguem-{i}", "errada-123");
            Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
            Assert.Equal(MensagemCredencialInvalida, (await resposta.Content.ReadFromJsonAsync<ErroResposta>())!.Mensagem);
        }
    }

    [Fact]
    public async Task Decima_primeira_tentativa_recebe_429_com_mensagem_e_Retry_After()
    {
        var cliente = await ClienteDoIpAsync("198.51.100.11");
        await GastarLimiteAsync(cliente);

        var resposta = await cliente.EntrarAsync("ninguem", "errada-123");

        Assert.Equal(HttpStatusCode.TooManyRequests, resposta.StatusCode);
        var erro = await resposta.Content.ReadFromJsonAsync<ErroResposta>();
        Assert.Equal(MensagemMuitasTentativas, erro!.Mensagem);
        Assert.Null(erro.Codigo);
        Assert.NotNull(resposta.Headers.RetryAfter?.Delta);
        Assert.InRange(resposta.Headers.RetryAfter!.Delta!.Value.TotalSeconds, 1, factory.JanelaDeLoginEmSegundos);
    }

    [Fact]
    public async Task Tentativa_recusada_nao_confere_senha_nem_conta_falha()
    {
        var login = await NovoUsuarioAsync("limite");
        var cliente = await ClienteDoIpAsync("198.51.100.12");
        await GastarLimiteAsync(cliente);

        factory.Hasher.Zerar();
        var resposta = await cliente.EntrarAsync(login, "errada-123");

        Assert.Equal(HttpStatusCode.TooManyRequests, resposta.StatusCode);
        Assert.Equal(0, factory.Hasher.Conferencias);
        Assert.Equal(0, (await BuscarAsync(login)).AccessFailedCount);
    }

    [Fact]
    public async Task Ips_diferentes_tem_limites_separados()
    {
        var login = await NovoUsuarioAsync("outroip");
        var esgotado = await ClienteDoIpAsync("198.51.100.13");
        await GastarLimiteAsync(esgotado);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await esgotado.EntrarAsync(login, Senha)).StatusCode);

        var outro = await ClienteDoIpAsync("198.51.100.14");

        Assert.Equal(HttpStatusCode.NoContent, (await outro.EntrarAsync(login, Senha)).StatusCode);
    }

    [Fact]
    public async Task Passada_a_janela_volta_a_aceitar()
    {
        var login = await NovoUsuarioAsync("janela");
        var cliente = await ClienteDoIpAsync("198.51.100.15");
        await GastarLimiteAsync(cliente);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await cliente.EntrarAsync(login, Senha)).StatusCode);

        await Task.Delay(TimeSpan.FromSeconds(factory.JanelaDeLoginEmSegundos + 1.5));

        Assert.Equal(HttpStatusCode.NoContent, (await cliente.EntrarAsync(login, Senha)).StatusCode);
    }

    [Fact]
    public async Task Outras_rotas_nao_sao_limitadas()
    {
        var cliente = await ClienteDoIpAsync("198.51.100.16");
        await GastarLimiteAsync(cliente);

        for (var i = 0; i < 15; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.GetAsync("/api/auth/me")).StatusCode);
        for (var i = 0; i < 15; i++)
            Assert.NotEqual(HttpStatusCode.TooManyRequests, (await cliente.SairAsync()).StatusCode);
    }
}
