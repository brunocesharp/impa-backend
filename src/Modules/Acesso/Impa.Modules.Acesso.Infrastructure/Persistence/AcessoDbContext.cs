using Impa.Modules.Acesso.Infrastructure.Identity;
using Impa.Modules.Acesso.Infrastructure.Sessoes;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Impa.Modules.Acesso.Infrastructure.Persistence;

/// <summary>Contexto do módulo Acesso. Grava só no schema <c>acesso</c> (ADR-001 / ADR-012).</summary>
public sealed class AcessoDbContext(DbContextOptions<AcessoDbContext> options)
    : IdentityDbContext<Usuario, Perfil, Guid>(options)
{
    public const string Schema = "acesso";

    public DbSet<Sessao> Sessoes => Set<Sessao>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); // mapeamento do Identity primeiro; os nomes abaixo vencem

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Usuario>(b =>
        {
            b.ToTable("usuarios");
            b.Property(u => u.NomeExibicao).HasMaxLength(Usuario.NomeExibicaoMaximo).IsRequired();
            // Sem HasDefaultValue: com default true, o EF trocaria um false explícito pelo default no insert.
            // Os usuários já existentes recebem true na migração BloqueioESessoes.
            b.Property(u => u.Ativo);
        });

        modelBuilder.Entity<Sessao>(b =>
        {
            b.ToTable("sessoes");
            b.HasKey(s => s.Id);
            b.Property(s => s.Id).ValueGeneratedNever();
            b.Property(s => s.Tiquete).IsRequired();
            b.HasOne<Usuario>().WithMany().HasForeignKey(s => s.UsuarioId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(s => s.UsuarioId); // desativar revoga todas as sessões do usuário
        });
        modelBuilder.Entity<Perfil>().ToTable("perfis");
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("usuarios_perfis");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("perfis_permissoes");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("usuarios_claims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("usuarios_logins");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("usuarios_tokens");
    }
}
