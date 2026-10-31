using Microsoft.EntityFrameworkCore;

namespace Impa.Host.Persistence;

/// <summary>
/// Contexto do Host: só o que é do banco como um todo (extensões do PostgreSQL).
/// Não tem tabelas de negócio; cada módulo tem o próprio contexto e schema.
/// </summary>
public sealed class HostDbContext(DbContextOptions<HostDbContext> options) : DbContext(options)
{
    public const string Schema = "host";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        // unaccent e pg_trgm: busca tolerante do catálogo (story 3.4).
        modelBuilder.HasPostgresExtension("public", "unaccent");
        modelBuilder.HasPostgresExtension("public", "pg_trgm");
    }
}
