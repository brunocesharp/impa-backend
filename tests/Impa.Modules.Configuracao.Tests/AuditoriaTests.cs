using Ardalis.Result;
using Impa.Modules.Configuracao.Application.Auditoria;
using Impa.Modules.Configuracao.Application.Auditoria.Queries.ConsultarAuditoria;
using Impa.Modules.Configuracao.Contracts.Auditoria;
using Impa.Modules.Configuracao.Domain.Auditoria;
using Impa.Shared.Messaging;

namespace Impa.Modules.Configuracao.Tests;

/// <summary>Story 4.4, T1/T3 — contrato do evento de auditoria e gravação sem dado sensível (CA-1, CA-2, CA-4).</summary>
public class AuditoriaTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 8, 10, 0, 0, TimeSpan.Zero);
    private readonly Guid _carla = Guid.NewGuid();
    private readonly RegistrosDeAuditoriaFake _registros = new();

    private OperacaoSensivelAuditada Evento(params CampoAlterado[] campos) =>
        new(_carla, "Carla", "Catalogo", "Item", "item-42", "custo.alterado", campos) { OcorreuEm = Agora };

    [Fact]
    public void Contrato_tem_tipo_e_versao_estaveis_e_volta_igual_da_serializacao()
    {
        var serializador = new SerializadorDeEventos();
        serializador.Registrar(typeof(OperacaoSensivelAuditada));
        var evento = Evento(new CampoAlterado("Custo", "10,00", "12,50"));

        var lido = (OperacaoSensivelAuditada)serializador.Desserializar(
            "auditoria.operacao-sensivel", 1, serializador.Serializar(evento));

        Assert.Equal("auditoria.operacao-sensivel", evento.Tipo);
        Assert.Equal(1, evento.Versao);
        Assert.Equal(evento.Id, lido.Id);
        Assert.Equal(Agora, lido.OcorreuEm);
        Assert.Equal(_carla, lido.UsuarioId);
        Assert.Equal("Carla", lido.UsuarioNome);
        Assert.Equal(("Catalogo", "Item", "item-42", "custo.alterado"), (lido.Modulo, lido.Entidade, lido.EntidadeId, lido.Operacao));
        var campo = Assert.Single(lido.Campos);
        Assert.Equal(("Custo", "10,00", "12,50", false), (campo.Campo, campo.Antes, campo.Depois, campo.Sensivel));
        Assert.Equal("Item:item-42", evento.ChaveDeCorrelacao);
    }

    [Fact]
    public async Task Handler_grava_o_registro_com_usuario_data_e_valores_antes_e_depois()
    {
        var evento = Evento(new CampoAlterado("Custo", "10,00", "12,50"));

        await new GravarOperacaoSensivel(_registros, new RelogioFixo(Agora.AddSeconds(3))).HandleAsync(evento, default);

        var registro = Assert.Single(_registros.Todos);
        Assert.Equal(evento.Id, registro.Id); // o id do evento: reentrega não vira outro registro
        Assert.Equal(Agora, registro.OcorreuEm);
        Assert.Equal(Agora.AddSeconds(3), registro.RegistradoEm);
        Assert.Equal((_carla, "Carla"), (registro.UsuarioId, registro.UsuarioNome));
        Assert.Equal(("Catalogo", "Item", "item-42", "custo.alterado"),
            (registro.Modulo, registro.Entidade, registro.EntidadeId, registro.Operacao));
        var campo = Assert.Single(registro.Campos);
        Assert.Equal(("Custo", "10,00", "12,50", false), (campo.Campo, campo.Antes, campo.Depois, campo.Omitido));
    }

    [Theory]
    [InlineData("Senha")]
    [InlineData("senhaDoCertificado")]
    [InlineData("Csc")]
    [InlineData("CSC")]
    [InlineData("TokenCsc")]
    [InlineData("Cpf")]
    [InlineData("cpf_do_cliente")]
    [InlineData("Telefone")]
    [InlineData("Celular")]
    [InlineData("Email")]
    [InlineData("E-mail")]
    [InlineData("Endereco")]
    [InlineData("Endereço")]
    [InlineData("Endereco.Logradouro")]
    [InlineData("Cep")]
    [InlineData("CscId")] // identificador do CSC: fica de fora junto com o CSC
    public void Campo_com_nome_sensivel_e_recusado_sem_valores(string nome)
    {
        Assert.True(CamposSensiveis.EhSensivel(nome));

        var campo = CampoAuditado.De(nome, "valor antigo", "valor novo", marcadoComoSensivel: false);

        Assert.Equal(nome, campo.Campo);
        Assert.Null(campo.Antes);
        Assert.Null(campo.Depois);
        Assert.True(campo.Omitido);
    }

    [Theory]
    [InlineData("Custo")]
    [InlineData("PrecoDeVenda")]
    [InlineData("Cfop")]
    [InlineData("Ncm")]
    [InlineData("Descricao")]
    [InlineData("Ambiente")]
    [InlineData("Cnpj")] // da empresa, não é dado pessoal da RN-14.6
    [InlineData("Excecao")] // contém "cep" no meio, mas não é a palavra
    public void Campo_comum_mantem_os_valores(string nome)
    {
        Assert.False(CamposSensiveis.EhSensivel(nome));

        var campo = CampoAuditado.De(nome, "a", "b", marcadoComoSensivel: false);

        Assert.Equal(("a", "b", false), (campo.Antes, campo.Depois, campo.Omitido));
    }

    [Fact]
    public async Task Campo_marcado_como_sensivel_pelo_modulo_e_recusado_mesmo_com_nome_comum()
    {
        var evento = Evento(
            new CampoAlterado("Ambiente", "Homologação", "Produção"),
            new CampoAlterado("ChaveDoCertificado", "abc", "def", Sensivel: true),
            new CampoAlterado("Senha", "antiga", "nova"),
            new CampoAlterado("Csc", "123456", "654321"));

        await new GravarOperacaoSensivel(_registros, new RelogioFixo(Agora)).HandleAsync(evento, default);

        var campos = Assert.Single(_registros.Todos).Campos;
        Assert.Equal(["Ambiente", "ChaveDoCertificado", "Senha", "Csc"], campos.Select(c => c.Campo));
        Assert.Equal(("Homologação", "Produção", false), (campos[0].Antes, campos[0].Depois, campos[0].Omitido));
        Assert.All(campos.Skip(1), c => Assert.Equal((null, null, true), (c.Antes, c.Depois, c.Omitido)));
        var valores = campos.SelectMany(c => new[] { c.Antes, c.Depois }).ToList();
        Assert.DoesNotContain("abc", valores);
        Assert.DoesNotContain("nova", valores);
        Assert.DoesNotContain("123456", valores);
    }

    [Fact]
    public void Registro_exige_usuario_modulo_entidade_e_operacao()
    {
        Assert.ThrowsAny<ArgumentException>(() => RegistroDeAuditoria.Criar(
            Guid.NewGuid(), Agora, Guid.Empty, "Carla", "Catalogo", "Item", "1", "custo.alterado", [], Agora));
        Assert.ThrowsAny<ArgumentException>(() => RegistroDeAuditoria.Criar(
            Guid.NewGuid(), Agora, _carla, "Carla", " ", "Item", "1", "custo.alterado", [], Agora));
        Assert.ThrowsAny<ArgumentException>(() => RegistroDeAuditoria.Criar(
            Guid.NewGuid(), Agora, _carla, "Carla", "Catalogo", "", "1", "custo.alterado", [], Agora));
        Assert.ThrowsAny<ArgumentException>(() => RegistroDeAuditoria.Criar(
            Guid.NewGuid(), Agora, _carla, "Carla", "Catalogo", "Item", "1", "", [], Agora));
    }

    [Fact]
    public void Valor_muito_longo_e_cortado_no_maximo()
    {
        var campo = CampoAuditado.De("Descricao", new string('a', 5000), "b", marcadoComoSensivel: false);

        Assert.Equal(CampoAuditado.ValorMaximo, campo.Antes!.Length);
    }

    [Fact]
    public void Consulta_valida_pagina_tamanho_e_periodo()
    {
        var validador = new ConsultarAuditoriaQueryValidator();

        Assert.True(validador.Validate(new ConsultarAuditoriaQuery(Agora.AddDays(-1), Agora, null, null, null, 1, 20)).IsValid);
        Assert.False(validador.Validate(new ConsultarAuditoriaQuery(null, null, null, null, null, 0, 20)).IsValid);
        Assert.False(validador.Validate(new ConsultarAuditoriaQuery(null, null, null, null, null, 1, 101)).IsValid);
        Assert.False(validador.Validate(new ConsultarAuditoriaQuery(Agora, Agora.AddDays(-1), null, null, null, 1, 20)).IsValid);
    }

    [Fact]
    public async Task Consulta_repassa_os_filtros_ao_repositorio()
    {
        var consulta = new ConsultarAuditoriaQuery(Agora.AddDays(-1), Agora, "carla", "Catalogo", "Item", 2, 10);

        var resultado = await new ConsultarAuditoriaQueryHandler(_registros).Handle(consulta, default);

        Assert.Equal(ResultStatus.Ok, resultado.Status);
        Assert.Equal(consulta, _registros.UltimaConsulta);
        Assert.Equal((2, 10, 0), (resultado.Value.Pagina, resultado.Value.TamanhoDaPagina, resultado.Value.Total));
    }
}

/// <summary>Registros em memória (a consulta real, com filtros no banco, é testada na integração).</summary>
public sealed class RegistrosDeAuditoriaFake : IRegistrosDeAuditoria
{
    public List<RegistroDeAuditoria> Todos { get; } = [];

    public ConsultarAuditoriaQuery? UltimaConsulta { get; private set; }

    public void Adicionar(RegistroDeAuditoria registro) => Todos.Add(registro);

    public Task<PaginaDeAuditoriaDto> ConsultarAsync(ConsultarAuditoriaQuery consulta, CancellationToken ct)
    {
        UltimaConsulta = consulta;
        return Task.FromResult(new PaginaDeAuditoriaDto([], consulta.Pagina, consulta.TamanhoDaPagina, 0));
    }
}
