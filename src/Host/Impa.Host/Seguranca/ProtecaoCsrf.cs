using Impa.Host.Http;
using Microsoft.AspNetCore.Antiforgery;

namespace Impa.Host.Seguranca;

/// <summary>
/// Proteção CSRF no padrão do Angular: o servidor entrega o token no cookie <c>XSRF-TOKEN</c> (legível pelo JS)
/// e exige o mesmo valor no cabeçalho <c>X-XSRF-TOKEN</c> de toda requisição <c>/api</c> que altera estado.
/// </summary>
public static class ProtecaoCsrf
{
    public const string CookieToken = "XSRF-TOKEN";
    public const string CabecalhoToken = "X-XSRF-TOKEN";
    public const string MensagemTokenInvalido = "Requisição recusada: token de segurança ausente ou inválido. Recarregue a página.";

    public static IServiceCollection AddProtecaoCsrf(this IServiceCollection services)
    {
        services.AddAntiforgery(options =>
        {
            options.HeaderName = CabecalhoToken;
            options.Cookie.Name = "impa.antiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });

        return services;
    }

    /// <summary>
    /// Depois da autenticação (o token é ligado à identidade) e antes da autorização: requisições seguras
    /// recebem um token novo; as que alteram estado, inclusive o login, precisam apresentar um válido.
    /// </summary>
    public static IApplicationBuilder UseProtecaoCsrf(this IApplicationBuilder app) =>
        app.Use(async (contexto, proximo) =>
        {
            if (!contexto.Request.Path.StartsWithSegments("/api"))
            {
                await proximo(contexto);
                return;
            }

            if (AlteraEstado(contexto.Request.Method))
            {
                var antiforgery = contexto.RequestServices.GetRequiredService<IAntiforgery>();
                if (!await antiforgery.IsRequestValidAsync(contexto))
                {
                    contexto.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await contexto.Response.WriteAsJsonAsync(new ErroDto(MensagemTokenInvalido));
                    return;
                }
            }
            else
            {
                EmitirToken(contexto);
            }

            await proximo(contexto);
        });

    /// <summary>Emite o token para a identidade atual de <see cref="HttpContext.User"/> (usar após login e logout).</summary>
    public static void EmitirToken(HttpContext contexto)
    {
        var tokens = contexto.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(contexto);
        contexto.Response.Cookies.Append(CookieToken, tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false, // a SPA lê o cookie e devolve o valor no cabeçalho
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
        });
    }

    private static bool AlteraEstado(string metodo) =>
        !(HttpMethods.IsGet(metodo) || HttpMethods.IsHead(metodo) || HttpMethods.IsOptions(metodo) || HttpMethods.IsTrace(metodo));
}
