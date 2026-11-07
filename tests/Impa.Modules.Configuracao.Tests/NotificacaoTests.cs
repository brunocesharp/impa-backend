using Impa.Modules.Configuracao.Domain.Notificacoes;

namespace Impa.Modules.Configuracao.Tests;

/// <summary>Story 4.2, T2 — notificação e leitura por usuário (CA-1, CA-2).</summary>
public class NotificacaoTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Carla = Guid.NewGuid();
    private static readonly Guid Diego = Guid.NewGuid();

    private static Notificacao CanetaAzul(params Guid[] destinatarios) =>
        Notificacao.Criar("teste.saldo", "Saldo negativo", "Caneta azul 0.7 ficou com saldo -1", "produto:caneta-azul-07",
            Agora, destinatarios);

    [Fact]
    public void Criar_guarda_tipo_titulo_texto_referencia_data_e_destinatarios()
    {
        var notificacao = CanetaAzul(Carla, Diego, Carla);

        Assert.NotEqual(Guid.Empty, notificacao.Id);
        Assert.Equal("teste.saldo", notificacao.Tipo);
        Assert.Equal("Saldo negativo", notificacao.Titulo);
        Assert.Equal("Caneta azul 0.7 ficou com saldo -1", notificacao.Texto);
        Assert.Equal("produto:caneta-azul-07", notificacao.ReferenciaDaOrigem);
        Assert.Equal(Agora, notificacao.CriadaEm);
        Assert.Equal([Carla, Diego], notificacao.Destinatarios.Select(d => d.UsuarioId));
        Assert.Empty(notificacao.Leituras);
    }

    [Fact]
    public void Cenario_2_5_marcar_como_lida_vale_so_para_quem_marcou()
    {
        var notificacao = CanetaAzul(Carla, Diego);

        notificacao.MarcarComoLida(Carla, Agora.AddMinutes(5));

        Assert.True(notificacao.LidaPor(Carla));
        Assert.False(notificacao.LidaPor(Diego));
        var leitura = Assert.Single(notificacao.Leituras);
        Assert.Equal(Carla, leitura.UsuarioId);
        Assert.Equal(Agora.AddMinutes(5), leitura.LidaEm);
    }

    [Fact]
    public void Marcar_de_novo_nao_duplica_nem_muda_a_data_da_leitura()
    {
        var notificacao = CanetaAzul(Carla);
        notificacao.MarcarComoLida(Carla, Agora.AddMinutes(1));

        notificacao.MarcarComoLida(Carla, Agora.AddMinutes(9));

        Assert.Equal(Agora.AddMinutes(1), Assert.Single(notificacao.Leituras).LidaEm);
    }

    [Fact]
    public void Quem_nao_e_destinatario_nao_marca_como_lida()
    {
        var notificacao = CanetaAzul(Carla);

        Assert.False(notificacao.EhDestinatario(Diego));
        Assert.Throws<InvalidOperationException>(() => notificacao.MarcarComoLida(Diego, Agora));
        Assert.Empty(notificacao.Leituras);
    }

    [Theory]
    [InlineData("", "Título", "Texto")]
    [InlineData("tipo", " ", "Texto")]
    [InlineData("tipo", "Título", "")]
    public void Tipo_titulo_e_texto_sao_obrigatorios(string tipo, string titulo, string texto)
    {
        Assert.ThrowsAny<ArgumentException>(() => Notificacao.Criar(tipo, titulo, texto, null, Agora, [Carla]));
    }

    [Fact]
    public void Referencia_em_branco_fica_nula()
    {
        Assert.Null(Notificacao.Criar("tipo", "Título", "Texto", "  ", Agora, [Carla]).ReferenciaDaOrigem);
    }
}
