using Impa.Modules.Configuracao.Domain.Notificacoes;
using Impa.Shared.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Impa.Modules.Configuracao.Infrastructure.Persistence;

/// <summary>
/// Contexto do módulo Configuração e Notificações. Grava só no schema <c>configuracao</c> (ADR-001 / ADR-012),
/// inclusive o Outbox (publica <c>NotificacaoCriada</c>) e o Inbox (consome os fatos que viram notificação).
/// </summary>
public sealed class ConfiguracaoDbContext(DbContextOptions<ConfiguracaoDbContext> options) : DbContext(options)
{
    public const string Schema = "configuracao";

    public DbSet<Notificacao> Notificacoes => Set<Notificacao>();

    public DbSet<NotificacaoDestinatario> NotificacoesDestinatarios => Set<NotificacaoDestinatario>();

    public DbSet<NotificacaoLida> NotificacoesLidas => Set<NotificacaoLida>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Notificacao>(b =>
        {
            b.ToTable("notificacoes");
            b.HasKey(n => n.Id);
            b.Property(n => n.Id).ValueGeneratedNever();
            b.Property(n => n.Tipo).HasMaxLength(Notificacao.TipoMaximo).IsRequired();
            b.Property(n => n.Titulo).HasMaxLength(Notificacao.TituloMaximo).IsRequired();
            b.Property(n => n.Texto).HasMaxLength(Notificacao.TextoMaximo).IsRequired();
            b.Property(n => n.ReferenciaDaOrigem).HasMaxLength(Notificacao.ReferenciaMaxima);
            b.HasIndex(n => n.CriadaEm);

            b.HasMany(n => n.Destinatarios).WithOne().HasForeignKey(d => d.NotificacaoId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(n => n.Leituras).WithOne().HasForeignKey(l => l.NotificacaoId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(n => n.Destinatarios).UsePropertyAccessMode(PropertyAccessMode.Field);
            b.Navigation(n => n.Leituras).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<NotificacaoDestinatario>(b =>
        {
            b.ToTable("notificacoes_destinatarios");
            b.HasKey(d => new { d.NotificacaoId, d.UsuarioId });
            b.HasIndex(d => d.UsuarioId); // lista e contador do usuário
        });

        // Uma leitura por usuário e notificação (RN-2.3: cada um marca as próprias).
        modelBuilder.Entity<NotificacaoLida>(b =>
        {
            b.ToTable("notificacoes_lidas");
            b.HasKey(l => new { l.NotificacaoId, l.UsuarioId });
            b.HasIndex(l => l.UsuarioId);
        });

        modelBuilder.MapearOutbox(Schema);
        modelBuilder.MapearInbox(Schema);
    }
}
