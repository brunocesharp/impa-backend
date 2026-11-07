using Impa.Messaging.Tests.Modulos;
using Impa.Shared.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Impa.Messaging.Tests;

/// <summary>CA-3 — decisão síncrona aguarda os eventos pendentes de uma chave, com tempo limite (T6, T8).</summary>
[Collection(ColecaoPostgres.Nome)]
public class EsperaPorChaveTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Sem_pendencia_retorna_logo()
    {
        await using var ambiente = await AmbienteDeMensageria.CriarAsync(postgres);
        await ambiente.IniciarAsync();
        var aguardar = ambiente.Servicos.GetRequiredService<IAguardarEventos>();

        var inicio = DateTime.UtcNow;
        Assert.True(await aguardar.AguardarAsync("item:nada", BaixarNoEstoque.Consumidor, TimeSpan.FromSeconds(5)));
        Assert.True(DateTime.UtcNow - inicio < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Com_pendencia_espera_ate_a_entrega_ou_o_tempo_limite()
    {
        await using var ambiente = await AmbienteDeMensageria.CriarAsync(postgres);
        ambiente.Controle.Bloquear(BaixarNoEstoque.Consumidor);
        await ambiente.IniciarAsync();
        var aguardar = ambiente.Servicos.GetRequiredService<IAguardarEventos>();

        await ambiente.RegistrarPedidoAsync("lapis");
        await ambiente.Controle.Entrou(BaixarNoEstoque.Consumidor).WaitAsync(TimeSpan.FromSeconds(10));

        // Pendente para o Estoque: o tempo limite vence.
        Assert.False(await aguardar.AguardarAsync("item:lapis", BaixarNoEstoque.Consumidor, TimeSpan.FromMilliseconds(300)));
        // Outra chave não espera por este evento.
        Assert.True(await aguardar.AguardarAsync("item:caderno", BaixarNoEstoque.Consumidor, TimeSpan.FromMilliseconds(300)));

        var espera = aguardar.AguardarAsync("item:lapis", BaixarNoEstoque.Consumidor, TimeSpan.FromSeconds(10));
        await Task.Delay(200);
        Assert.False(espera.IsCompleted);
        ambiente.Controle.Liberar(BaixarNoEstoque.Consumidor);

        Assert.True(await espera);
        Assert.Equal(1, await ambiente.NoContextoAsync<EstoqueDbContext, int>(c =>
            Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(c.Efeitos)));
    }

    [Fact]
    public async Task Pendencia_de_outro_consumidor_nao_segura_a_espera()
    {
        await using var ambiente = await AmbienteDeMensageria.CriarAsync(postgres);
        ambiente.Controle.Bloquear(RegistrarNoFiscal.Consumidor);
        await ambiente.IniciarAsync();
        var aguardar = ambiente.Servicos.GetRequiredService<IAguardarEventos>();

        await ambiente.RegistrarPedidoAsync("borracha");
        await ambiente.Controle.Entrou(RegistrarNoFiscal.Consumidor).WaitAsync(TimeSpan.FromSeconds(10));

        // O Estoque já processou (Inbox gravado), mesmo com o Fiscal travado e o evento ainda pendente.
        Assert.True(await aguardar.AguardarAsync("item:borracha", BaixarNoEstoque.Consumidor, TimeSpan.FromSeconds(10)));
        Assert.False(await aguardar.AguardarAsync("item:borracha", RegistrarNoFiscal.Consumidor, TimeSpan.FromMilliseconds(300)));
    }

    [Fact]
    public async Task Evento_com_falha_continua_pendente_para_o_consumidor()
    {
        await using var ambiente = await AmbienteDeMensageria.CriarAsync(postgres);
        ambiente.Controle.Falhar(RegistrarNoFiscal.Consumidor, true);
        await ambiente.IniciarAsync();
        var aguardar = ambiente.Servicos.GetRequiredService<IAguardarEventos>();

        var evento = await ambiente.RegistrarPedidoAsync("regua");
        await AmbienteDeMensageria.AteAsync(async () =>
            (await ambiente.OutboxAsync(evento.Id))?.Situacao == SituacaoDoEvento.ComFalha);

        Assert.False(await aguardar.AguardarAsync("item:regua", RegistrarNoFiscal.Consumidor, TimeSpan.FromMilliseconds(300)));
    }

    [Fact]
    public async Task Consumidor_sem_handler_registrado_e_recusado()
    {
        await using var ambiente = await AmbienteDeMensageria.CriarAsync(postgres);
        await ambiente.IniciarAsync();
        var aguardar = ambiente.Servicos.GetRequiredService<IAguardarEventos>();

        // Nome errado não pode virar "nada pendente" em silêncio (a decisão síncrona seguiria sem esperar).
        var erro = await Assert.ThrowsAsync<ArgumentException>(() =>
            aguardar.AguardarAsync("item:caneta", "Estoqe", TimeSpan.FromSeconds(1)));
        Assert.Contains("Estoqe", erro.Message);
    }
}
