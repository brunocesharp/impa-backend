using Microsoft.EntityFrameworkCore;

namespace Impa.Shared.Messaging;

/// <summary>Nomes das tabelas de mensageria; os mesmos em todo schema de módulo.</summary>
public static class TabelasDeMensageria
{
    public const string Outbox = "outbox";
    public const string Inbox = "inbox";
}

/// <summary>
/// Mapeamento EF reutilizável do Outbox e do Inbox no schema do módulo (ADR-011 / ADR-012):
/// no <c>OnModelCreating</c> do contexto do módulo, <c>modelBuilder.MapearOutbox(Schema)</c> (módulo que publica)
/// e <c>modelBuilder.MapearInbox(Schema)</c> (módulo que consome). Colunas com nome fixo: o despachante usa SQL.
/// </summary>
public static class MapeamentoDeMensageria
{
    public const int TipoMaximo = 200;
    public const int ChaveMaxima = 200;
    public const int HandlerMaximo = 300;

    public static ModelBuilder MapearOutbox(this ModelBuilder modelBuilder, string schema)
    {
        modelBuilder.Entity<OutboxMensagem>(b =>
        {
            b.ToTable(TabelasDeMensageria.Outbox, schema);
            b.HasKey(m => m.Id);
            b.Property(m => m.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(m => m.Tipo).HasColumnName("tipo").HasMaxLength(TipoMaximo).IsRequired();
            b.Property(m => m.Versao).HasColumnName("versao");
            b.Property(m => m.ChaveDeCorrelacao).HasColumnName("chave_de_correlacao").HasMaxLength(ChaveMaxima);
            b.Property(m => m.Conteudo).HasColumnName("conteudo").HasColumnType("jsonb").IsRequired();
            b.Property(m => m.OcorreuEm).HasColumnName("ocorreu_em");
            b.Property(m => m.Situacao).HasColumnName("situacao").HasConversion<string>().HasMaxLength(20);
            b.Property(m => m.Tentativas).HasColumnName("tentativas");
            b.Property(m => m.ProximaTentativaEm).HasColumnName("proxima_tentativa_em");
            b.Property(m => m.UltimoErro).HasColumnName("ultimo_erro");
            b.Property(m => m.EntregueEm).HasColumnName("entregue_em");

            b.HasIndex(m => new { m.Situacao, m.ProximaTentativaEm }); // lote do despachante
            b.HasIndex(m => m.ChaveDeCorrelacao); // espera por chave (ADR-012, regra 5)
        });

        return modelBuilder;
    }

    public static ModelBuilder MapearInbox(this ModelBuilder modelBuilder, string schema)
    {
        modelBuilder.Entity<InboxMensagem>(b =>
        {
            b.ToTable(TabelasDeMensageria.Inbox, schema);
            b.HasKey(m => new { m.EventoId, m.Handler });
            b.Property(m => m.EventoId).HasColumnName("evento_id");
            b.Property(m => m.Handler).HasColumnName("handler").HasMaxLength(HandlerMaximo);
            b.Property(m => m.ProcessadoEm).HasColumnName("processado_em");
        });

        return modelBuilder;
    }
}
