using System.Globalization;
using System.Threading.RateLimiting;
using Impa.Host.Http;

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

    private const int SegmentosPorJanela = 6;

    /// <summary>Partição dos clientes sem IP conhecido (ex.: TestServer): compartilham um único limite.</summary>
    private const string SemIp = "sem-ip";

    public static IServiceCollection AddLimiteDeLogin(this IServiceCollection services, IConfiguration configuration)
    {
        var secao = configuration.GetSection("Autenticacao:LimiteDeLogin");
        var tentativas = secao.GetValue("Tentativas", 10);
        var janela = TimeSpan.FromSeconds(secao.GetValue("JanelaEmSegundos", 60));

        services.AddRateLimiter(options =>
        {
            options.AddPolicy(Politica, contexto => RateLimitPartition.GetSlidingWindowLimiter(
                contexto.Connection.RemoteIpAddress?.ToString() ?? SemIp,
                _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = tentativas,
                    Window = janela,
                    SegmentsPerWindow = SegmentosPorJanela,
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));

            options.OnRejected = async (rejeicao, ct) =>
            {
                var resposta = rejeicao.HttpContext.Response;
                resposta.StatusCode = StatusCodes.Status429TooManyRequests;

                // Sem a estimativa do limitador, o pior caso: o próximo segmento da janela.
                var aguardar = rejeicao.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                    ? retryAfter
                    : janela / SegmentosPorJanela;
                resposta.Headers.RetryAfter = Math.Max(1, (long)Math.Ceiling(aguardar.TotalSeconds))
                    .ToString(NumberFormatInfo.InvariantInfo);

                await resposta.WriteAsJsonAsync(new ErroDto(MensagemMuitasTentativas), ct);
            };
        });

        return services;
    }

    /// <summary>Depois de <c>UseForwardedHeaders</c> (IP real do cliente) e antes dos endpoints.</summary>
    public static IApplicationBuilder UseLimiteDeLogin(this IApplicationBuilder app) => app.UseRateLimiter();
}
