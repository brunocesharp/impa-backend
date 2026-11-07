using Impa.Host.Messaging;
using Impa.Messaging.Tests.Modulos;
using Impa.Shared.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Impa.Messaging.Tests;

/// <summary>CA-2 — novas tentativas com intervalo crescente, "com falha" após o limite e reprocessamento (T5, T8).</summary>
[Collection(ColecaoPostgres.Nome)]
public class FalhaEReprocessamentoTests(PostgresFixture postgres)
{
    [Fact]
    public void Intervalos_padrao_crescem_e_o_limite_e_10()
    {
        var opcoes = new OpcoesDaMensageria();

        Assert.Equal(10, opcoes.LimiteDeTentativas);
        Assert.Equal(TimeSpan.FromSeconds(5), opcoes.IntervaloDeVerificacao);
        Assert.Equal(TimeSpan.FromSeconds(1), opcoes.IntervaloAposTentativa(1));
        Assert.Equal(TimeSpan.FromSeconds(5), opcoes.IntervaloAposTentativa(2));
        Assert.Equal(TimeSpan.FromSeconds(30), opcoes.IntervaloAposTentativa(3));
        Assert.Equal(TimeSpan.FromMinutes(2), opcoes.IntervaloAposTentativa(4));
        for (var t = 2; t < opcoes.LimiteDeTentativas; t++)
            Assert.True(opcoes.IntervaloAposTentativa(t) > opcoes.IntervaloAposTentativa(t - 1));
        Assert.Equal(opcoes.IntervaloAposTentativa(9), opcoes.IntervaloAposTentativa(50)); // além da lista: o último
    }

    [Fact]
    public async Task Falha_tenta_de_novo_ate_o_limite_fica_com_falha_e_pode_ser_reprocessado()
    {
        await using var ambiente = await AmbienteDeMensageria.CriarAsync(postgres);
        ambiente.Controle.Falhar(RegistrarNoFiscal.Consumidor, true);
        await ambiente.IniciarAsync();

        var evento = await ambiente.RegistrarPedidoAsync();
        await AmbienteDeMensageria.AteAsync(async () =>
            (await ambiente.OutboxAsync(evento.Id))?.Situacao == SituacaoDoEvento.ComFalha, mensagem: "não ficou com falha");

        var comFalha = (await ambiente.OutboxAsync(evento.Id))!;
        Assert.Equal(AmbienteDeMensageria.LimiteDeTentativas, comFalha.Tentativas);
        Assert.Contains("falha forçada no Fiscal", comFalha.UltimoErro);
        Assert.Equal(AmbienteDeMensageria.LimiteDeTentativas, ambiente.Controle.Execucoes(RegistrarNoFiscal.Consumidor));
        // O consumidor que deu certo não repete o efeito nas novas tentativas.
        Assert.Equal(1, ambiente.Controle.Execucoes(BaixarNoEstoque.Consumidor));
        Assert.Equal(0, await ambiente.EfeitosAsync<FiscalDbContext>(evento.PedidoId));

        // Depois do limite, ninguém tenta mais sozinho.
        await Task.Delay(500);
        Assert.Equal(AmbienteDeMensageria.LimiteDeTentativas, ambiente.Controle.Execucoes(RegistrarNoFiscal.Consumidor));

        ambiente.Controle.Falhar(RegistrarNoFiscal.Consumidor, false);
        var reprocessador = ambiente.Servicos.GetRequiredService<ReprocessadorDeEventos>();
        Assert.True(await reprocessador.ReprocessarAsync(evento.Id, CancellationToken.None));
        await ambiente.EntregueAsync(evento.Id);

        Assert.Equal(1, await ambiente.EfeitosAsync<FiscalDbContext>(evento.PedidoId));
        Assert.Equal(1, await ambiente.EfeitosAsync<EstoqueDbContext>(evento.PedidoId));
    }

    [Fact]
    public async Task Tentativas_respeitam_o_intervalo_crescente()
    {
        // Relógio parado: a próxima tentativa só vence quando o teste avança o tempo, e o intervalo
        // aplicado pelo despachante pode ser conferido exatamente (segundos inteiros: o timestamptz guarda µs).
        var relogio = new FakeTimeProvider(new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero));
        await using var ambiente = await AmbienteDeMensageria.CriarAsync(postgres, relogio: relogio);
        ambiente.Controle.Falhar(RegistrarNoFiscal.Consumidor, true);
        await ambiente.IniciarAsync();

        var evento = await ambiente.RegistrarPedidoAsync();
        var inicio = relogio.GetUtcNow();
        await AmbienteDeMensageria.AteAsync(async () => (await ambiente.OutboxAsync(evento.Id))?.Tentativas == 1);
        var primeira = (await ambiente.OutboxAsync(evento.Id))!;

        Assert.Equal(SituacaoDoEvento.Pendente, primeira.Situacao);
        Assert.Equal(inicio + AmbienteDeMensageria.IntervaloAposTentativa(1), primeira.ProximaTentativaEm);
        Assert.Contains("falha forçada", primeira.UltimoErro);

        // Antes de vencer o intervalo, ninguém tenta de novo.
        await Task.Delay(300);
        Assert.Equal(1, (await ambiente.OutboxAsync(evento.Id))!.Tentativas);

        relogio.Advance(AmbienteDeMensageria.IntervaloAposTentativa(1));
        var segundaFalha = relogio.GetUtcNow();
        await AmbienteDeMensageria.AteAsync(async () => (await ambiente.OutboxAsync(evento.Id))?.Tentativas == 2);
        var segunda = (await ambiente.OutboxAsync(evento.Id))!;

        Assert.Equal(segundaFalha + AmbienteDeMensageria.IntervaloAposTentativa(2), segunda.ProximaTentativaEm);
        Assert.True(AmbienteDeMensageria.IntervaloAposTentativa(2) > AmbienteDeMensageria.IntervaloAposTentativa(1));
    }

    [Fact]
    public async Task Reprocessar_evento_que_nao_esta_com_falha_nao_faz_nada()
    {
        await using var ambiente = await AmbienteDeMensageria.CriarAsync(postgres);
        await ambiente.IniciarAsync();
        var evento = await ambiente.RegistrarPedidoAsync();
        await ambiente.EntregueAsync(evento.Id);
        var reprocessador = ambiente.Servicos.GetRequiredService<ReprocessadorDeEventos>();

        Assert.False(await reprocessador.ReprocessarAsync(evento.Id, CancellationToken.None));
        Assert.False(await reprocessador.ReprocessarAsync(Guid.NewGuid(), CancellationToken.None));
    }
}
