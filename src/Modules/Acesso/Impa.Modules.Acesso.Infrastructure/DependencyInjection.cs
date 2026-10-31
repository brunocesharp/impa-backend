using Impa.Modules.Acesso.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Impa.Modules.Acesso.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAcessoModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AcessoDbContext>(options => options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", AcessoDbContext.Schema)));

        return services;
    }
}
