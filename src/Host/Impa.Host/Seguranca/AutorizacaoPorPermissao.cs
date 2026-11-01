using Impa.Modules.Acesso.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace Impa.Host.Seguranca;

/// <summary>RN-1.10: as permissões são verificadas no servidor; esconder um botão na tela não basta.</summary>
public static class AutorizacaoPorPermissao
{
    /// <summary>Negar por padrão: todo endpoint exige sessão, exceto os marcados com <c>AllowAnonymous</c>.</summary>
    public static IServiceCollection AddAutorizacaoPorPermissao(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return services;
    }

    /// <summary>Exige sessão e a permissão indicada (claim <c>permissao</c> do perfil). Sem ela: 403.</summary>
    public static TBuilder RequirePermissao<TBuilder>(this TBuilder builder, string permissao)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(politica => politica
            .RequireAuthenticatedUser()
            .RequireClaim(Permissoes.ClaimType, permissao));
}
