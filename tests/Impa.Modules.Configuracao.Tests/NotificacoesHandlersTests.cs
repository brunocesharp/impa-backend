using Ardalis.Result;
using Impa.Modules.Configuracao.Application.Notificacoes;
using Impa.Modules.Configuracao.Application.Notificacoes.Commands.MarcarComoLida;
using Impa.Modules.Configuracao.Application.Notificacoes.Commands.MarcarTodasComoLidas;
using Impa.Modules.Configuracao.Application.Notificacoes.Queries.ContarNaoLidas;
using Impa.Modules.Configuracao.Application.Notificacoes.Queries.ListarNotificacoes;
using Impa.Modules.Configuracao.Domain.Notificacoes;

namespace Impa.Modules.Configuracao.Tests;

/// <summary>Story 4.2, T4 — casos de uso da central de notificações do usuário da sessão (CA-1, CA-2).</summary>
public class NotificacoesHandlersTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);
    private readonly Guid _carla = Guid.NewGuid();
    private readonly Guid _diego = Guid.NewGuid();
    private readonly NotificacoesFake _notificacoes = new();
    private readonly RelogioFixo _relogio = new(Agora);

    private Notificacao Nova(string texto, DateTimeOffset criadaEm, params Guid[] destinatarios)
    {
        var notificacao = Notificacao.Criar("teste.saldo-negativo", "Saldo negativo", texto, null, criadaEm, destinatarios);
        _notificacoes.Adicionar(notificacao);
        return notificacao;
    }

    private Task<int> NaoLidasAsync(Guid usuario) =>
        new ContarNaoLidasQueryHandler(_notificacoes).Handle(new ContarNaoLidasQuery(usuario), default)
            .ContinueWith(t => t.Result.Value.NaoLidas);

    [Fact]
    public async Task Cenario_2_5_Carla_marca_como_lida_e_o_contador_de_Diego_continua_1()
    {
        var notificacao = Nova("Caneta azul 0.7 ficou com saldo -1", Agora, _carla, _diego);
        Assert.Equal(1, await NaoLidasAsync(_carla));
        Assert.Equal(1, await NaoLidasAsync(_diego));

        var resultado = await new MarcarComoLidaCommandHandler(_notificacoes, _relogio)
            .Handle(new MarcarComoLidaCommand(notificacao.Id, _carla), default);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(1, _notificacoes.Salvamentos);
        Assert.Equal(0, await NaoLidasAsync(_carla));
        Assert.Equal(1, await NaoLidasAsync(_diego));
    }

    [Fact]
    public async Task Marcar_notificacao_de_outro_usuario_ou_inexistente_responde_nao_encontrada()
    {
        var soDaCarla = Nova("Caneta azul 0.7 ficou com saldo -1", Agora, _carla);
        var handler = new MarcarComoLidaCommandHandler(_notificacoes, _relogio);

        var deOutro = await handler.Handle(new MarcarComoLidaCommand(soDaCarla.Id, _diego), default);
        var inexistente = await handler.Handle(new MarcarComoLidaCommand(Guid.NewGuid(), _carla), default);

        Assert.Equal(ResultStatus.NotFound, deOutro.Status);
        Assert.Equal(ResultStatus.NotFound, inexistente.Status);
        Assert.False(soDaCarla.LidaPor(_diego));
        Assert.Equal(0, _notificacoes.Salvamentos);
    }

    [Fact]
    public async Task Marcar_todas_como_lidas_zera_so_o_contador_de_quem_marcou()
    {
        Nova("Caneta azul 0.7 ficou com saldo -1", Agora, _carla, _diego);
        Nova("Borracha ficou com saldo -2", Agora.AddMinutes(1), _carla, _diego);

        var resultado = await new MarcarTodasComoLidasCommandHandler(_notificacoes, _relogio)
            .Handle(new MarcarTodasComoLidasCommand(_carla), default);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(0, await NaoLidasAsync(_carla));
        Assert.Equal(2, await NaoLidasAsync(_diego));
    }

    [Fact]
    public async Task Lista_so_as_do_usuario_mais_recentes_primeiro_com_a_leitura_dele()
    {
        var antiga = Nova("Caneta azul 0.7 ficou com saldo -1", Agora, _carla, _diego);
        Nova("Borracha ficou com saldo -2", Agora.AddMinutes(1), _carla);
        Nova("Só do Diego", Agora.AddMinutes(2), _diego);
        antiga.MarcarComoLida(_carla, Agora);

        var pagina = (await new ListarNotificacoesQueryHandler(_notificacoes)
            .Handle(new ListarNotificacoesQuery(_carla, 1, 20), default)).Value;

        Assert.Equal(2, pagina.Total);
        Assert.Equal(["Borracha ficou com saldo -2", "Caneta azul 0.7 ficou com saldo -1"], pagina.Itens.Select(i => i.Texto));
        Assert.Equal([false, true], pagina.Itens.Select(i => i.Lida));
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, ListarNotificacoesQuery.TamanhoMaximoDaPagina + 1)]
    public void Pagina_e_tamanho_fora_do_limite_sao_invalidos(int pagina, int tamanho)
    {
        var validacao = new ListarNotificacoesQueryValidator().Validate(new ListarNotificacoesQuery(_carla, pagina, tamanho));

        Assert.False(validacao.IsValid);
    }
}
