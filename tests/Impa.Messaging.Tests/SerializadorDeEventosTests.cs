using Impa.Shared.Messaging;

namespace Impa.Messaging.Tests;

[EventoDeIntegracao("teste.pedido-criado", 1)]
public sealed record PedidoCriadoV1(Guid PedidoId, decimal Valor) : IntegrationEvent;

[EventoDeIntegracao("teste.pedido-criado", 2)]
public sealed record PedidoCriadoV2(Guid PedidoId, decimal Valor, string Canal) : IntegrationEvent
{
    public override string? ChaveDeCorrelacao => $"pedido:{PedidoId}";
}

public sealed record EventoSemAtributo : IntegrationEvent;

public class SerializadorDeEventosTests
{
    [Fact]
    public void Tipo_e_versao_vem_do_atributo_do_evento()
    {
        var evento = new PedidoCriadoV2(Guid.NewGuid(), 10m, "pdv");

        Assert.Equal("teste.pedido-criado", evento.Tipo);
        Assert.Equal(2, evento.Versao);
        Assert.Equal($"pedido:{evento.PedidoId}", evento.ChaveDeCorrelacao);
        Assert.Null(new PedidoCriadoV1(Guid.NewGuid(), 1m).ChaveDeCorrelacao);
    }

    [Fact]
    public void Cada_evento_tem_identificador_unico_e_data()
    {
        var a = new PedidoCriadoV1(Guid.NewGuid(), 1m);
        var b = new PedidoCriadoV1(Guid.NewGuid(), 1m);

        Assert.NotEqual(Guid.Empty, a.Id);
        Assert.NotEqual(a.Id, b.Id);
        Assert.True(a.OcorreuEm > DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.Equal(TimeSpan.Zero, a.OcorreuEm.Offset);
    }

    [Fact]
    public void Ida_e_volta_preserva_o_evento_pela_versao()
    {
        var serializador = new SerializadorDeEventos();
        serializador.Registrar(typeof(PedidoCriadoV1));
        serializador.Registrar(typeof(PedidoCriadoV2));
        var v1 = new PedidoCriadoV1(Guid.NewGuid(), 12.5m);
        var v2 = new PedidoCriadoV2(Guid.NewGuid(), 7m, "web");

        var lidoV1 = serializador.Desserializar(v1.Tipo, v1.Versao, serializador.Serializar(v1));
        var lidoV2 = serializador.Desserializar(v2.Tipo, v2.Versao, serializador.Serializar(v2));

        Assert.Equal(v1, Assert.IsType<PedidoCriadoV1>(lidoV1));
        Assert.Equal(v2, Assert.IsType<PedidoCriadoV2>(lidoV2));
    }

    [Fact]
    public void Tipo_ou_versao_desconhecidos_nao_sao_conhecidos()
    {
        var serializador = new SerializadorDeEventos();
        serializador.Registrar(typeof(PedidoCriadoV1));

        Assert.True(serializador.Conhece("teste.pedido-criado", 1));
        Assert.False(serializador.Conhece("teste.pedido-criado", 2));
        Assert.Throws<InvalidOperationException>(() => serializador.Desserializar("teste.pedido-criado", 2, "{}"));
    }

    [Fact]
    public void Evento_sem_atributo_e_recusado()
    {
        Assert.Throws<InvalidOperationException>(() => new EventoSemAtributo().Tipo);
        Assert.Throws<InvalidOperationException>(() => new SerializadorDeEventos().Registrar(typeof(EventoSemAtributo)));
    }

    [Fact]
    public void Mesmo_tipo_e_versao_em_duas_classes_e_recusado()
    {
        var serializador = new SerializadorDeEventos();
        serializador.Registrar(typeof(PedidoCriadoV1));
        serializador.Registrar(typeof(PedidoCriadoV1)); // mesma classe: sem efeito

        Assert.Throws<InvalidOperationException>(() => serializador.Registrar(typeof(DuplicadoDoPedidoCriadoV1)));
    }

    [EventoDeIntegracao("teste.pedido-criado", 1)]
    private sealed record DuplicadoDoPedidoCriadoV1 : IntegrationEvent;
}
