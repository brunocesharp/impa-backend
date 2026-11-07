using Impa.Modules.Acesso.Infrastructure.Sessoes;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

namespace Impa.Host.Seguranca;

/// <summary>Sessão por cookie do Identity (ADR-006).</summary>
public static class AutenticacaoPorCookie
{
    public const string NomeCookie = "impa.sessao";

    /// <summary>RN-1.3: a sessão termina no logout ou após 12 horas, o que vier primeiro.</summary>
    public static readonly TimeSpan DuracaoDaSessao = TimeSpan.FromHours(12);

    public static IServiceCollection AddAutenticacaoPorCookie(this IServiceCollection services)
    {
        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = NomeCookie;
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;

            // Tempo contado do login: sem expiração deslizante. A renovação feita pelo SecurityStampValidator
            // mantém o fim original quando SlidingExpiration = false.
            options.ExpireTimeSpan = DuracaoDaSessao;
            options.SlidingExpiration = false;

            // API: responde com o status em vez de redirecionar para uma tela de login.
            options.Events.OnRedirectToLogin = contexto =>
            {
                contexto.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = contexto =>
            {
                contexto.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        // Sessões no servidor (acesso.sessoes): o cookie leva só a chave; logout e desativação revogam (RN-1.7).
        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Configure<SessaoTicketStore>((options, store) => options.SessionStore = store);

        return services;
    }
}
