using Impa.Host.Persistence;
using Impa.Modules.Acesso.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Impa")
    ?? throw new InvalidOperationException("ConnectionStrings:Impa não configurada.");

builder.Services.AddDbContext<HostDbContext>(options => options.UseNpgsql(
    connectionString,
    npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", HostDbContext.Schema)));

builder.Services.AddAcessoModule(connectionString);

builder.Services.AddHealthChecks().AddDbContextCheck<HostDbContext>("banco");

var app = builder.Build();

await app.MigrateDatabaseAsync();

app.MapHealthChecks("/api/health");

app.Run();

public partial class Program;
