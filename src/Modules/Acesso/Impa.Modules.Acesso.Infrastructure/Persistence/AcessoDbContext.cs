using Impa.Modules.Acesso.Infrastructure.Identity;
using Impa.Modules.Acesso.Infrastructure.Sessoes;
using Impa.Modules.Acesso.Infrastructure.Terminais;
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

    public DbSet<Terminal> Terminais => Set<Terminal>();

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
            b.Property(u => u.TrocaDeSenhaObrigatoria); // os existentes recebem false na migração GestaoDeUsuarios
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
        modelBuilder.Entity<Terminal>(b =>
        {
            b.ToTable("terminais");
            b.HasKey(t => t.Id);
            b.Property(t => t.Id).ValueGeneratedNever();
            b.Property(t => t.Nome).HasMaxLength(Terminal.NomeMaximo).IsRequired();
            b.Property(t => t.NomeNormalizado).HasMaxLength(Terminal.NomeMaximo).IsRequired();
            b.Property(t => t.TokenHash).IsRequired();
            b.HasIndex(t => t.TokenHash).IsUnique(); // busca do terminal a cada requisição de PDV/caixa
            // Nome único só entre os ativos: revogado o terminal, o mesmo nome pode ser registrado de novo.
            b.HasIndex(t => t.NomeNormalizado).IsUnique().HasFilter("\"RevogadoEm\" IS NULL");
            b.HasOne<Usuario>().WithMany().HasForeignKey(t => t.CriadoPor).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<Perfil>(b =>
        {
            b.ToTable("perfis");
            b.Property(p => p.Protegido); // sem default: os perfis existentes recebem false na migração
        });
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("usuarios_perfis");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("perfis_permissoes");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("usuarios_claims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("usuarios_logins");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("usuarios_tokens");
    }
}
