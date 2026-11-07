using Impa.Modules.Acesso.Contracts;
using Impa.Modules.Configuracao.Application.Notificacoes;
using Impa.Modules.Configuracao.Contracts.Notificacoes;
using Impa.Shared.Messaging;

namespace Impa.Modules.Configuracao.Tests;

/// <summary>Fato de teste com o texto do cenário 2.5 (o fato real, estoque negativo, é da story 7-2).</summary>
[EventoDeIntegracao("teste.saldo-ficou-negativo", 1)]
public sealed record SaldoFicouNegativoDeTeste(string Item, int Saldo) : IntegrationEvent
{
    public override string? ChaveDeCorrelacao => $"item:{Item}";
}

public sealed class NotificarSaldoNegativoDeTeste(CriacaoDeNotificacoes criacao)
    : GeradorDeNotificacao<SaldoFicouNegativoDeTeste>(criacao)
{
    protected override string Tipo => "teste.saldo-negativo";

    protected override string Titulo(SaldoFicouNegativoDeTeste evento) => "Saldo negativo";

    protected override string Texto(SaldoFicouNegativoDeTeste evento) => $"{evento.Item} ficou com saldo {evento.Saldo}";
}

/// <summary>Story 4.2, T2/T3 — destinatários no momento da criação e base dos handlers de fato (CA-2).</summary>
public class GeradorDeNotificacaoTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);
    private readonly Guid _carla = Guid.NewGuid();
    private readonly Guid _diego = Guid.NewGuid();
    private readonly UsuariosComPermissaoFake _usuarios = new();
    private readonly NotificacoesFake _notificacoes = new();
    private readonly EventosFake _eventos = new();

    private NotificarSaldoNegativoDeTeste Handler() =>
        new(new CriacaoDeNotificacoes(_notificacoes, _usuarios, _eventos, new RelogioFixo(Agora)));

    [Fact]
    public async Task Fato_vira_notificacao_para_os_usuarios_com_notificacoes_receber()
    {
        _usuarios.PorPermissao[Permissoes.NotificacoesReceber] = [_carla, _diego];

        await Handler().HandleAsync(new SaldoFicouNegativoDeTeste("Caneta azul 0.7", -1), CancellationToken.None);

        var notificacao = Assert.Single(_notificacoes.Todas);
        Assert.Equal("teste.saldo-negativo", notificacao.Tipo);
        Assert.Equal("Saldo negativo", notificacao.Titulo);
        Assert.Equal("Caneta azul 0.7 ficou com saldo -1", notificacao.Texto);
        Assert.Equal("item:Caneta azul 0.7", notificacao.ReferenciaDaOrigem);
        Assert.Equal(Agora, notificacao.CriadaEm);
        Assert.Equal([_carla, _diego], notificacao.Destinatarios.Select(d => d.UsuarioId));
        Assert.Equal([Permissoes.NotificacoesReceber], _usuarios.Consultadas);
    }

    [Fact]
    public async Task Destinatarios_sao_os_que_tem_a_permissao_no_momento_da_criacao()
    {
        _usuarios.PorPermissao[Permissoes.NotificacoesReceber] = [_carla];
        await Handler().HandleAsync(new SaldoFicouNegativoDeTeste("Caneta azul 0.7", -1), CancellationToken.None);

        _usuarios.PorPermissao[Permissoes.NotificacoesReceber] = [_carla, _diego]; // Diego ganhou a permissão depois
        await Handler().HandleAsync(new SaldoFicouNegativoDeTeste("Borracha", -2), CancellationToken.None);

        Assert.Equal([_carla], _notificacoes.Todas[0].Destinatarios.Select(d => d.UsuarioId));
        Assert.Equal([_carla, _diego], _notificacoes.Todas[1].Destinatarios.Select(d => d.UsuarioId));
    }

    [Fact]
    public async Task Publica_NotificacaoCriada_com_tipo_e_destinatarios()
    {
        _usuarios.PorPermissao[Permissoes.NotificacoesReceber] = [_carla, _diego];

        await Handler().HandleAsync(new SaldoFicouNegativoDeTeste("Caneta azul 0.7", -1), CancellationToken.None);

        var criada = Assert.IsType<NotificacaoCriada>(Assert.Single(_eventos.Publicados));
        var notificacao = _notificacoes.Todas.Single();
        Assert.Equal(notificacao.Id, criada.NotificacaoId);
        Assert.Equal("teste.saldo-negativo", criada.TipoDaNotificacao);
        Assert.Equal("Saldo negativo", criada.Titulo);
        Assert.Equal("Caneta azul 0.7 ficou com saldo -1", criada.Texto);
        Assert.Equal([_carla, _diego], criada.Destinatarios);
        Assert.Equal("configuracao.notificacao-criada", criada.Tipo);
    }

    [Fact]
    public async Task Nao_salva_por_conta_propria_o_despachante_grava_junto_com_o_Inbox()
    {
        _usuarios.PorPermissao[Permissoes.NotificacoesReceber] = [_carla];

        await Handler().HandleAsync(new SaldoFicouNegativoDeTeste("Caneta azul 0.7", -1), CancellationToken.None);

        Assert.Equal(0, _notificacoes.Salvamentos);
    }
}
