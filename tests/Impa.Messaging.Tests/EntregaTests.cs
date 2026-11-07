using Impa.Messaging.Tests.Modulos;
using Impa.Shared.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Impa.Messaging.Tests;

/// <summary>CA-1 — publicação na mesma transação e entrega exatamente uma vez a cada consumidor (T2, T3, T4, T8).</summary>
[Collection(ColecaoPostgres.Nome)]
public class EntregaTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Evento_publicado_e_entregue_uma_vez_a_cada_consumidor()
    {
        await using var ambiente = await AmbienteDeMensageria.CriarAsync(postgres);
        await ambiente.IniciarAsync();

        var evento = await ambiente.RegistrarPedidoAsync();
        await ambiente.EntregueAsync(evento.Id);

        Assert.Equal(1, await ambiente.EfeitosAsync<EstoqueDbContext>(evento.PedidoId));
        Assert.Equal(1, await ambiente.EfeitosAsync<FiscalDbContext>(evento.PedidoId));
        var outbox = (await ambiente.OutboxAsync(evento.Id))!;
        Assert.Equal("pedidos.pedido-registrado", outbox.Tipo);
        Assert.Equal(1, outbox.Versao);
        Assert.Equal("item:caneta", outbox.ChaveDeCorrelacao);
        Assert.NotNull(outbox.EntregueEm);
        Assert.Equal(2, await ambiente.NoContextoAsync<EstoqueDbContext, int>(c => c.Inbox.CountAsync()) +
                        await ambiente.NoContextoAsync<FiscalDbContext, int>(c => c.Inbox.CountAsync()));
    }

    [Fact]
    public async Task Despachante_e_acordado_logo_apos_o_commit()
    {
        // Verificação periódica longa: só o sinal após o commit explica uma entrega rápida.
        await using var ambiente = await AmbienteDeMensageria.CriarAsync(postgres, intervaloDeVerificacao: TimeSpan.FromMinutes(10));
        await ambiente.IniciarAsync();
        await Task.Delay(300); // a primeira varredura (vazia) termina e o despachante passa a esperar

        var evento = await ambiente.RegistrarPedidoAsync();

        await ambiente.EntregueAsync(evento.Id, TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Transacao_desfeita_nao_publica()
    {
        await using var ambiente = await AmbienteDeMensageria.CriarAsync(postgres);
        await ambiente.IniciarAsync();

        var evento = await ambiente.RegistrarPedidoAsync(desfazer: true);
        await Task.Delay(500);

        Assert.Null(await ambiente.OutboxAsync(evento.Id));
        Assert.Equal(0, await ambiente.NoContextoAsync<PedidosDbContext, int>(c => c.Pedidos.CountAsync()));
        Assert.Equal(0, ambiente.Controle.Execucoes(BaixarNoEstoque.Consumidor));
    }

    [Fact]
    public async Task Reentrega_do_mesmo_evento_e_ignorada_pelo_inbox()
    {
        await using var ambiente = await AmbienteDeMensageria.CriarAsync(postgres);
        await ambiente.IniciarAsync();
        var evento = await ambiente.RegistrarPedidoAsync();
        await ambiente.EntregueAsync(evento.Id);

        // Simula a reentrega: o evento volta a pendente (ex.: queda depois do efeito e antes de marcar entregue).
        await ambiente.NoContextoAsync<PedidosDbContext, int>(c => c.Outbox
            .Where(m => m.Id == evento.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Situacao, SituacaoDoEvento.Pendente)
                .SetProperty(m => m.EntregueEm, (DateTimeOffset?)null)));
        await ambiente.EntregueAsync(evento.Id);

        Assert.Equal(1, await ambiente.EfeitosAsync<EstoqueDbContext>(evento.PedidoId));
        Assert.Equal(1, await ambiente.EfeitosAsync<FiscalDbContext>(evento.PedidoId));
        Assert.Equal(1, ambiente.Controle.Execucoes(BaixarNoEstoque.Consumidor));
        Assert.Equal(1, ambiente.Controle.Execucoes(RegistrarNoFiscal.Consumidor));
    }

    [Fact]
    public async Task Reinicio_no_meio_da_entrega_nao_perde_nem_duplica_o_evento()
    {
        await using var ambiente = await AmbienteDeMensageria.CriarAsync(postgres);
        ambiente.Controle.Bloquear(BaixarNoEstoque.Consumidor);
        await ambiente.IniciarAsync();

        var evento = await ambiente.RegistrarPedidoAsync();
        await ambiente.Controle.Entrou(BaixarNoEstoque.Consumidor).WaitAsync(TimeSpan.FromSeconds(10));
        await ambiente.PararAsync(); // a API cai com o handler no meio do efeito

        await ambiente.IniciarAsync(); // sobe de novo, ainda sem o efeito gravado
        Assert.Equal(0, await ambiente.EfeitosAsync<EstoqueDbContext>(evento.PedidoId));
        Assert.Equal(SituacaoDoEvento.Pendente, (await ambiente.OutboxAsync(evento.Id))!.Situacao);

        ambiente.Controle.Liberar(BaixarNoEstoque.Consumidor);
        await ambiente.EntregueAsync(evento.Id);

        Assert.Equal(1, await ambiente.EfeitosAsync<EstoqueDbContext>(evento.PedidoId));
        Assert.Equal(1, await ambiente.EfeitosAsync<FiscalDbContext>(evento.PedidoId));
        Assert.Equal(0, (await ambiente.OutboxAsync(evento.Id))!.Tentativas); // cancelamento não conta como falha
    }

    [Fact]
    public async Task Varios_eventos_sao_todos_entregues()
    {
        await using var ambiente = await AmbienteDeMensageria.CriarAsync(postgres);
        await ambiente.IniciarAsync();

        var eventos = new List<PedidoRegistrado>();
        for (var i = 0; i < 120; i++) // mais de um lote
            eventos.Add(await ambiente.RegistrarPedidoAsync($"item-{i}"));

        await AmbienteDeMensageria.AteAsync(async () =>
            await ambiente.NoContextoAsync<PedidosDbContext, int>(c =>
                c.Outbox.CountAsync(m => m.Situacao == SituacaoDoEvento.Entregue)) == eventos.Count);
        Assert.Equal(eventos.Count, await ambiente.NoContextoAsync<EstoqueDbContext, int>(c => c.Efeitos.CountAsync()));
    }

    [Fact]
    public async Task Handler_lento_num_evento_nao_segura_o_evento_anterior_do_mesmo_lote()
    {
        await using var ambiente = await AmbienteDeMensageria.CriarAsync(postgres);
        // Segura o despachante num primeiro evento para os dois seguintes ficarem pendentes juntos.
        ambiente.Controle.Bloquear(BaixarNoEstoque.Consumidor, "porta");
        ambiente.Controle.Bloquear(RegistrarNoFiscal.Consumidor, "lento");
        await ambiente.IniciarAsync();

        var porta = await ambiente.RegistrarPedidoAsync("porta");
        await ambiente.Controle.Entrou(BaixarNoEstoque.Consumidor, "porta").WaitAsync(TimeSpan.FromSeconds(10));
        var rapido = await ambiente.RegistrarPedidoAsync("rapido");
        await ambiente.RegistrarPedidoAsync("lento");
        ambiente.Controle.Liberar(BaixarNoEstoque.Consumidor, "porta");
        await ambiente.EntregueAsync(porta.Id);

        // "rapido" e "lento" são lidos depois de "porta"; com o Fiscal travado em "lento", "rapido" já
        // precisa aparecer como entregue (commit por evento, não no fim do lote).
        await ambiente.Controle.Entrou(RegistrarNoFiscal.Consumidor, "lento").WaitAsync(TimeSpan.FromSeconds(10));
        await ambiente.EntregueAsync(rapido.Id, TimeSpan.FromSeconds(3));
    }
}
