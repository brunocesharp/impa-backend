using Impa.Modules.Acesso.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Impa.Host.Persistence;

public static class DatabaseMigrations
{
    /// <summary>Aplica as migrações do Host (extensões) e de cada módulo, nesta ordem.</summary>
    public static async Task MigrateDatabaseAsync(this WebApplication app)
    {
        if (!app.Configuration.GetValue("Database:MigrateOnStartup", true))
            return;

        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<HostDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<AcessoDbContext>().Database.MigrateAsync();
    }
}
