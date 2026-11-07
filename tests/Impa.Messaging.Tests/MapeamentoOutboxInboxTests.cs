using Impa.Shared.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Impa.Messaging.Tests;

public class MapeamentoOutboxInboxTests
{
    private sealed class ContextoDeModulo(DbContextOptions<ContextoDeModulo> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("pedidos");
            modelBuilder.MapearOutbox("pedidos");
            modelBuilder.MapearInbox("pedidos");
        }
    }

    private static IModel Modelo()
    {
        var options = new DbContextOptionsBuilder<ContextoDeModulo>().UseNpgsql("Host=localhost").Options;
        using var contexto = new ContextoDeModulo(options);
        return contexto.GetService<IDesignTimeModel>().Model;
    }

    [Fact]
    public void Outbox_fica_no_schema_do_modulo()
    {
        var outbox = Modelo().FindEntityType(typeof(OutboxMensagem))!;

        Assert.Equal("pedidos", outbox.GetSchema());
        Assert.Equal(TabelasDeMensageria.Outbox, outbox.GetTableName());
        Assert.Equal([nameof(OutboxMensagem.Id)], outbox.FindPrimaryKey()!.Properties.Select(p => p.Name));
        Assert.Equal("jsonb", outbox.FindProperty(nameof(OutboxMensagem.Conteudo))!.GetColumnType());
        Assert.Contains(outbox.GetIndexes(), i => i.Properties.Select(p => p.Name)
            .SequenceEqual([nameof(OutboxMensagem.Situacao), nameof(OutboxMensagem.ProximaTentativaEm)]));
        Assert.Contains(outbox.GetIndexes(), i => i.Properties.Single().Name == nameof(OutboxMensagem.ChaveDeCorrelacao));
    }

    [Fact]
    public void Inbox_fica_no_schema_do_modulo_com_chave_evento_e_handler()
    {
        var inbox = Modelo().FindEntityType(typeof(InboxMensagem))!;

        Assert.Equal("pedidos", inbox.GetSchema());
        Assert.Equal(TabelasDeMensageria.Inbox, inbox.GetTableName());
        Assert.Equal(
            [nameof(InboxMensagem.EventoId), nameof(InboxMensagem.Handler)],
            inbox.FindPrimaryKey()!.Properties.Select(p => p.Name));
    }

    [Fact]
    public void Nova_mensagem_do_outbox_guarda_o_evento_pendente()
    {
        var evento = new PedidoCriadoV2(Guid.NewGuid(), 3m, "pdv");
        var agora = DateTimeOffset.UtcNow;

        var mensagem = OutboxMensagem.Nova(evento, "{\"x\":1}", agora);

        Assert.Equal(evento.Id, mensagem.Id);
        Assert.Equal(evento.Tipo, mensagem.Tipo);
        Assert.Equal(evento.Versao, mensagem.Versao);
        Assert.Equal(evento.ChaveDeCorrelacao, mensagem.ChaveDeCorrelacao);
        Assert.Equal(evento.OcorreuEm, mensagem.OcorreuEm);
        Assert.Equal("{\"x\":1}", mensagem.Conteudo);
        Assert.Equal(SituacaoDoEvento.Pendente, mensagem.Situacao);
        Assert.Equal(0, mensagem.Tentativas);
        Assert.Equal(agora, mensagem.ProximaTentativaEm);
        Assert.Null(mensagem.EntregueEm);
        Assert.Null(mensagem.UltimoErro);
    }
}
