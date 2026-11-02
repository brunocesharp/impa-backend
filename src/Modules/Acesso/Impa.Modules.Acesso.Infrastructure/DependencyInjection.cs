using Impa.Modules.Acesso.Application.Autenticacao;
using Impa.Modules.Acesso.Infrastructure.Identity;
using Impa.Modules.Acesso.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Impa.Modules.Acesso.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registra o módulo Acesso: banco, Identity (usuários, perfis, SignInManager) e a sessão.
    /// O esquema de autenticação por cookie é configurado pelo Host.
    /// </summary>
    public static IdentityBuilder AddAcessoModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AcessoDbContext>(options => options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", AcessoDbContext.Schema)));

        services.AddHttpContextAccessor();
        services.AddSingleton<HashFicticio>();
        services.AddScoped<ISessaoService, SessaoService>();

        return services
            .AddIdentityCore<Usuario>(options =>
            {
                // RN-1.1: mínimo de 8 caracteres, sem exigir dígito, símbolo ou maiúscula.
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireLowercase = false;
                options.Password.RequiredUniqueChars = 1;
                options.User.RequireUniqueEmail = false;
            })
            .AddRoles<Perfil>()
            .AddEntityFrameworkStores<AcessoDbContext>()
            .AddSignInManager();
    }

    /// <summary>
    /// Prepara o módulo antes da primeira requisição (chamado pelo Host depois do <c>Build</c>):
    /// calcula o hash fictício do login de usuário inexistente.
    /// </summary>
    public static void PrepararAcesso(this IServiceProvider servicos) =>
        servicos.GetRequiredService<HashFicticio>().Preparar(servicos);
}
