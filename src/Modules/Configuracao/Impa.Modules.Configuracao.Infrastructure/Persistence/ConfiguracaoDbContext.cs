using Impa.Modules.Configuracao.Domain.Auditoria;
using Impa.Modules.Configuracao.Domain.Notificacoes;
using Impa.Shared.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Impa.Modules.Configuracao.Infrastructure.Persistence;

/// <summary>
/// Contexto do módulo Configuração e Notificações. Grava só no schema <c>configuracao</c> (ADR-001 / ADR-012),
/// inclusive o Outbox (publica <c>NotificacaoCriada</c>) e o Inbox (consome os fatos que viram notificação e os
/// eventos de auditoria).
/// </summary>
public sealed class ConfiguracaoDbContext(DbContextOptions<ConfiguracaoDbContext> options) : DbContext(options)
{
    public const string Schema = "configuracao";

    public DbSet<Notificacao> Notificacoes => Set<Notificacao>();

    public DbSet<NotificacaoDestinatario> NotificacoesDestinatarios => Set<NotificacaoDestinatario>();

    public DbSet<NotificacaoLida> NotificacoesLidas => Set<NotificacaoLida>();

    /// <summary>Operações sensíveis (story 4.4), gravadas a partir do evento de auditoria de cada módulo.</summary>
    public DbSet<RegistroDeAuditoria> Auditoria => Set<RegistroDeAuditoria>();

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

        // Auditoria (M1.6, story 4.4): id = id do evento; os campos alterados ficam num jsonb.
        modelBuilder.Entity<RegistroDeAuditoria>(b =>
        {
            b.ToTable("auditoria");
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).ValueGeneratedNever();
            b.Property(r => r.UsuarioNome).HasMaxLength(RegistroDeAuditoria.UsuarioNomeMaximo).IsRequired();
            b.Property(r => r.Modulo).HasMaxLength(RegistroDeAuditoria.ModuloMaximo).IsRequired();
            b.Property(r => r.Entidade).HasMaxLength(RegistroDeAuditoria.EntidadeMaxima).IsRequired();
            b.Property(r => r.EntidadeId).HasMaxLength(RegistroDeAuditoria.EntidadeIdMaximo).IsRequired();
            b.Property(r => r.Operacao).HasMaxLength(RegistroDeAuditoria.OperacaoMaxima).IsRequired();
            b.HasIndex(r => r.OcorreuEm); // consulta por período, mais recentes primeiro
            b.HasIndex(r => r.UsuarioId);
            b.HasIndex(r => new { r.Modulo, r.Entidade });

            b.OwnsMany(r => r.Campos, c =>
            {
                c.ToJson("campos");
                c.Property(x => x.Campo).HasMaxLength(CampoAuditado.CampoMaximo);
            });
            b.Navigation(r => r.Campos).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.MapearOutbox(Schema);
        modelBuilder.MapearInbox(Schema);
    }
}
