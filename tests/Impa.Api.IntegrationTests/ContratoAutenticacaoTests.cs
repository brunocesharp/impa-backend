using Impa.Host.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Impa.Api.IntegrationTests;

/// <summary>
/// Respostas declaradas de <c>/api/auth/*</c> (R17): a SPA trata o 400 do login pela forma do corpo
/// (<c>ValidationProblem</c> × <c>{ mensagem }</c> do CSRF) e o logout com sessão vencida devolve 400.
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
}
