using System.Net;
using System.Text.Json;
using Impa.Host.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Impa.Api.IntegrationTests;

/// <summary>
/// Contrato de <c>/api/auth/*</c> com a SPA (R17, R22): a SPA trata o 400 do login pela forma do corpo
/// (<c>ValidationProblem</c> com <c>errors.Usuario</c>/<c>errors.Senha</c> × <c>{ mensagem }</c> do CSRF) e trata
/// 400/401 do logout como sessão já encerrada. Os testes conferem o corpo real, não só os metadados.
/// </summary>
public class ContratoAutenticacaoTests(ImpaApiFactory factory) : IClassFixture<ImpaApiFactory>
{
    private IReadOnlyList<IProducesResponseTypeMetadata> Respostas(string rota)
    {
        _ = factory.CriarCliente();
        var endpoint = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == rota);
        return endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>();
    }

    private static async Task<JsonElement> CorpoAsync(HttpResponseMessage resposta) =>
        JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;

    private static void AssertMensagemNaoVazia(JsonElement corpo)
    {
        Assert.True(corpo.TryGetProperty("mensagem", out var mensagem), $"Corpo sem 'mensagem': {corpo}");
        Assert.False(string.IsNullOrWhiteSpace(mensagem.GetString()));
    }

    [Fact]
    public void Login_declara_os_dois_400_validacao_e_csrf()
    {
        var respostas400 = Respostas("/api/auth/login").Where(r => r.StatusCode == StatusCodes.Status400BadRequest).ToList();

        Assert.Contains(respostas400, r => r.Type == typeof(HttpValidationProblemDetails));
        Assert.Contains(respostas400, r => r.Type == typeof(ErroDto));
    }

    [Fact]
    public void Logout_declara_o_400_da_sessao_vencida()
    {
        var respostas = Respostas("/api/auth/logout");

        Assert.Contains(respostas, r => r.StatusCode == StatusCodes.Status400BadRequest && r.Type == typeof(ErroDto));
    }

    [Fact]
    public async Task Login_sem_token_xsrf_responde_400_com_mensagem_e_sem_errors()
    {
        var cliente = await ClienteApi.AbrirAsync(factory);

        var login = await cliente.PostAsync("/api/auth/login", new { usuario = "ana", senha = "balcao2026" }, enviarXsrf: false);

        Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
        var corpo = await CorpoAsync(login);
        AssertMensagemNaoVazia(corpo);
        Assert.False(corpo.TryGetProperty("errors", out _)); // a SPA usa a ausência de errors para a mensagem geral
    }

    [Fact]
    public async Task Login_em_branco_responde_400_com_errors_Usuario_e_Senha()
    {
        var cliente = await ClienteApi.AbrirAsync(factory);

        var login = await cliente.EntrarAsync("   ", "");

        Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
        var errors = (await CorpoAsync(login)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("Usuario", out var usuario), $"errors sem 'Usuario': {errors}");
        Assert.True(errors.TryGetProperty("Senha", out var senha), $"errors sem 'Senha': {errors}");
        Assert.NotEmpty(usuario.EnumerateArray().ToList());
        Assert.NotEmpty(senha.EnumerateArray().ToList());
    }

    [Fact]
    public async Task Logout_depois_das_12_horas_com_o_token_antigo_responde_400_com_mensagem()
    {
        var cliente = await ClienteApi.AbrirAsync(factory);
        await cliente.EntrarAsync(ImpaApiFactory.AdminUsuario, ImpaApiFactory.AdminSenha);

        // O relógio só avança (FakeTimeProvider); os outros testes desta classe abrem sessões novas e não dependem dele.
        factory.Clock.Advance(TimeSpan.FromHours(12).Add(TimeSpan.FromMinutes(1)));

        var logout = await cliente.SairAsync(); // token XSRF ainda da identidade anterior

        Assert.Equal(HttpStatusCode.BadRequest, logout.StatusCode);
        AssertMensagemNaoVazia(await CorpoAsync(logout));
    }

    [Fact]
    public async Task Logout_sem_sessao_responde_401()
    {
        var cliente = await ClienteApi.AbrirAsync(factory); // token XSRF anônimo válido

        var logout = await cliente.SairAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, logout.StatusCode);
    }
}
