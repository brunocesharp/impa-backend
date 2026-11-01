using Impa.Modules.Acesso.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Impa.Modules.Acesso.Infrastructure.Persistence;

/// <summary>Contexto do módulo Acesso. Grava só no schema <c>acesso</c> (ADR-001 / ADR-012).</summary>
public sealed class AcessoDbContext(DbContextOptions<AcessoDbContext> options)
    : IdentityDbContext<Usuario, Perfil, Guid>(options)
{
    public const string Schema = "acesso";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); // mapeamento do Identity primeiro; os nomes abaixo vencem

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Usuario>(b =>
        {
            b.ToTable("usuarios");
            b.Property(u => u.NomeExibicao).HasMaxLength(Usuario.NomeExibicaoMaximo).IsRequired();
        });
        modelBuilder.Entity<Perfil>().ToTable("perfis");
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("usuarios_perfis");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("perfis_permissoes");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("usuarios_claims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("usuarios_logins");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("usuarios_tokens");
    }
}
