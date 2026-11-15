using System.Globalization;
using System.Threading.RateLimiting;
using Impa.Host.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace Impa.Host.Seguranca;

/// <summary>
/// Limite de tentativas de login por origem (story 1-10): cada IP de cliente pode fazer até
/// <c>Autenticacao:LimiteDeLogin:Tentativas</c> logins numa janela deslizante de
/// <c>Autenticacao:LimiteDeLogin:JanelaEmSegundos</c>; acima disso, 429 sem chegar ao caso de uso.
/// Independente do bloqueio por conta (RN-1.2). O IP vem do <c>X-Forwarded-For</c> do Caddy:
/// <see cref="UseLimiteDeLogin"/> precisa rodar depois de <c>UseForwardedHeaders</c>.
/// </summary>
public static class LimiteDeLogin
{
    public const string Politica = "limite-de-login";
    public const string MensagemMuitasTentativas = "Muitas tentativas de entrar. Aguarde um minuto e tente novamente";

    /// <summary>
    /// Recuperação de senha (<c>esqueci-senha</c>, <c>redefinir-senha</c>, story 1-8): contador próprio por IP,
    /// <c>Autenticacao:LimiteDeRecuperacao</c>, para os pedidos não gastarem as tentativas de login dos caixas atrás do
    /// mesmo NAT (R5 da revisão 1).
    /// </summary>
    public const string PoliticaDeRecuperacao = "limite-de-recuperacao";
    public const string MensagemMuitosPedidos = "Muitas tentativas. Aguarde um minuto e tente novamente";

    private const int SegmentosPorJanela = 6;

    /// <summary>Partição dos clientes sem IP conhecido (ex.: TestServer): compartilham um único limite.</summary>
    private const string SemIp = "sem-ip";

    public static IServiceCollection AddLimiteDeLogin(this IServiceCollection services, IConfiguration configuration)
    {
        var login = Ler(configuration, "Autenticacao:LimiteDeLogin");
        var recuperacao = Ler(configuration, "Autenticacao:LimiteDeRecuperacao");

        services.AddRateLimiter(options =>
        {
            options.AddPolicy(Politica, contexto => PorIp(contexto, login));
            options.AddPolicy(PoliticaDeRecuperacao, contexto => PorIp(contexto, recuperacao));

            options.OnRejected = async (rejeicao, ct) =>
            {
                var resposta = rejeicao.HttpContext.Response;
                resposta.StatusCode = StatusCodes.Status429TooManyRequests;

                var deRecuperacao = rejeicao.HttpContext.GetEndpoint()?.Metadata
                    .GetMetadata<EnableRateLimitingAttribute>()?.PolicyName == PoliticaDeRecuperacao;
                var janela = deRecuperacao ? recuperacao.Janela : login.Janela;

                // Sem a estimativa do limitador, o pior caso: o próximo segmento da janela.
                var aguardar = rejeicao.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                    ? retryAfter
                    : janela / SegmentosPorJanela;
                resposta.Headers.RetryAfter = Math.Max(1, (long)Math.Ceiling(aguardar.TotalSeconds))
                    .ToString(NumberFormatInfo.InvariantInfo);

                await resposta.WriteAsJsonAsync(new ErroDto(deRecuperacao ? MensagemMuitosPedidos : MensagemMuitasTentativas), ct);
            };
        });

        return services;
    }

    private sealed record Limite(int Tentativas, TimeSpan Janela);

    private static Limite Ler(IConfiguration configuration, string secao)
    {
        var valores = configuration.GetSection(secao);
        return new Limite(valores.GetValue("Tentativas", 10), TimeSpan.FromSeconds(valores.GetValue("JanelaEmSegundos", 60)));
    }

    private static RateLimitPartition<string> PorIp(HttpContext contexto, Limite limite) =>
        RateLimitPartition.GetSlidingWindowLimiter(
            contexto.Connection.RemoteIpAddress?.ToString() ?? SemIp,
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = limite.Tentativas,
                Window = limite.Janela,
                SegmentsPerWindow = SegmentosPorJanela,
                QueueLimit = 0,
                AutoReplenishment = true,
            });

    /// <summary>Depois de <c>UseForwardedHeaders</c> (IP real do cliente) e antes dos endpoints.</summary>
    public static IApplicationBuilder UseLimiteDeLogin(this IApplicationBuilder app) => app.UseRateLimiter();
}
