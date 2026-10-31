using Microsoft.EntityFrameworkCore;

namespace Impa.Modules.Acesso.Infrastructure.Persistence;

/// <summary>Contexto do módulo Acesso. Grava só no schema <c>acesso</c> (ADR-001 / ADR-012).</summary>
public sealed class AcessoDbContext(DbContextOptions<AcessoDbContext> options) : DbContext(options)
{
    public const string Schema = "acesso";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AcessoDbContext).Assembly);
    }
}
