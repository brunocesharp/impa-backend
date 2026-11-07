using System.Net.Http.Json;

namespace Impa.Api.IntegrationTests;

/// <summary>
/// Cliente HTTP que se comporta como a SPA: guarda os cookies, lê o cookie <c>XSRF-TOKEN</c>
/// de cada resposta e o devolve no cabeçalho <c>X-XSRF-TOKEN</c> das requisições que alteram estado.
/// </summary>
public sealed class ClienteApi(HttpClient http)
{
    public const string CookieXsrf = "XSRF-TOKEN";
    public const string CabecalhoXsrf = "X-XSRF-TOKEN";

    public string? TokenXsrf { get; private set; }

    /// <summary>Último <c>Set-Cookie</c> recebido para cada nome de cookie.</summary>
    public Dictionary<string, string> UltimosSetCookie { get; } = new(StringComparer.Ordinal);

    /// <summary>Abre o cliente como a SPA faz ao carregar: consulta a sessão, o que já entrega o token XSRF.</summary>
    public static async Task<ClienteApi> AbrirAsync(ImpaApiFactory factory)
    {
        var cliente = new ClienteApi(factory.CriarCliente());
        await cliente.GetAsync("/api/auth/me");
        return cliente;
    }

    public async Task<HttpResponseMessage> GetAsync(string url) => Capturar(await http.GetAsync(url));

    public Task<HttpResponseMessage> PostAsync(string url, object? corpo = null, bool enviarXsrf = true) =>
        EnviarAsync(HttpMethod.Post, url, corpo, enviarXsrf);

    public Task<HttpResponseMessage> PutAsync(string url, object? corpo = null) => EnviarAsync(HttpMethod.Put, url, corpo);

    public Task<HttpResponseMessage> DeleteAsync(string url) => EnviarAsync(HttpMethod.Delete, url, null);

    public async Task<HttpResponseMessage> EnviarAsync(HttpMethod metodo, string url, object? corpo, bool enviarXsrf = true)
    {
        using var requisicao = new HttpRequestMessage(metodo, url)
        {
            Content = corpo is null ? null : JsonContent.Create(corpo),
        };
        if (enviarXsrf && TokenXsrf is not null)
            requisicao.Headers.Add(CabecalhoXsrf, TokenXsrf);

        return Capturar(await http.SendAsync(requisicao));
    }

    public Task<HttpResponseMessage> EntrarAsync(string usuario, string senha) =>
        PostAsync("/api/auth/login", new { usuario, senha });

    public Task<HttpResponseMessage> SairAsync() => PostAsync("/api/auth/logout");

    private HttpResponseMessage Capturar(HttpResponseMessage resposta)
    {
        if (!resposta.Headers.TryGetValues("Set-Cookie", out var cookies))
            return resposta;

        foreach (var cookie in cookies)
        {
            var nomeValor = cookie.Split(';', 2)[0];
            var separador = nomeValor.IndexOf('=');
            var nome = nomeValor[..separador];
            UltimosSetCookie[nome] = cookie;

            if (nome == CookieXsrf)
                TokenXsrf = Uri.UnescapeDataString(nomeValor[(separador + 1)..]);
        }

        return resposta;
    }
}
