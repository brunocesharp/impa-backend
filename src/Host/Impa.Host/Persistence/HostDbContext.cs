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
        // Sem schema explícito: o PostgreSQL cria em `public` (search_path). Com "public" explícito o
        // snapshot do Npgsql omite o schema e o EF acusa PendingModelChangesWarning a cada inicialização.
        modelBuilder.HasPostgresExtension("unaccent");
        modelBuilder.HasPostgresExtension("pg_trgm");
    }
}
