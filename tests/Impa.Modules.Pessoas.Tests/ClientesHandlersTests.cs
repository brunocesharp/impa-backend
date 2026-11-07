using Ardalis.Result;
using Impa.Modules.Pessoas.Application.Clientes.Commands;
using Impa.Modules.Pessoas.Application.Comum;
using Impa.Modules.Pessoas.Domain;
using Impa.Shared;

namespace Impa.Modules.Pessoas.Tests;

/// <summary>Story 3-7 — regras do cliente (RN-14.1), cenários 14.2 e 14.3 nos handlers e validators.</summary>
public class ClientesHandlersTests
{
    private readonly ClientesEmMemoria _clientes = new();
    private readonly TimeProvider _relogio = TimeProvider.System;

    private static CadastrarClienteCommand Novo(string nome, string? documento = null, string? telefone = null, string? email = null) =>
        new(nome, documento, telefone, email, null, null);

    private async Task<Result<Application.Clientes.ClienteDto>> CadastrarAsync(CadastrarClienteCommand comando)
    {
        var validacao = await new CadastrarClienteCommandValidator().ValidateAsync(comando);
        if (!validacao.IsValid)
            return Result.Invalid(validacao.Errors.Select(e => new ValidationError(e.PropertyName, e.ErrorMessage)));
        return await new CadastrarClienteCommandHandler(_clientes, _relogio).Handle(comando, CancellationToken.None);
    }

    private void TemMaria() =>
        _clientes.Itens.Add(Cliente.Criar(new DadosDoCliente("Maria Souza", Documento.Criar("529.982.247-25"), null, null, null, null),
            DateTimeOffset.UtcNow));

    [Fact]
    public async Task Cenario_14_1_cliente_cadastrado_com_cpf_e_telefone()
    {
        var resultado = await CadastrarAsync(Novo("Maria Souza", "529.982.247-25", "(31) 99999-0000"));

        Assert.Equal(ResultStatus.Created, resultado.Status);
        Assert.Equal("529.982.247-25", resultado.Value.Documento);
        Assert.Equal("31999990000", resultado.Value.Telefone);
        var salvo = Assert.Single(_clientes.Itens);
        Assert.Equal("52998224725", salvo.Documento);
        Assert.True(salvo.Ativo);
    }

    // ---- Esquema do cenário 14.2 ----

    [Fact]
    public async Task Cenario_14_2_cliente_sem_documento_e_salvo()
    {
        TemMaria();
        var resultado = await CadastrarAsync(Novo("João Lima"));

        Assert.Equal(ResultStatus.Created, resultado.Status);
        Assert.Null(resultado.Value.Documento);
    }

    [Fact]
    public async Task Cenario_14_2_cpf_repetido_e_recusado_com_o_nome_do_dono()
    {
        TemMaria();
        var resultado = await CadastrarAsync(Novo("João Lima", "529.982.247-25"));

        Assert.Equal(ResultStatus.Conflict, resultado.Status);
        Assert.Equal("CPF já cadastrado para Maria Souza", Assert.Single(resultado.Errors));
    }

    [Fact]
    public async Task Cenario_14_2_cpf_invalido_e_recusado()
    {
        TemMaria();
        var resultado = await CadastrarAsync(Novo("João Lima", "111.111.111-11"));

        Assert.Equal(ResultStatus.Invalid, resultado.Status);
        var erro = Assert.Single(resultado.ValidationErrors);
        Assert.Equal("Documento", erro.Identifier);
        Assert.Equal("CPF inválido", erro.ErrorMessage);
    }

    [Fact]
    public async Task CNPJ_invalido_e_repetido_no_cliente_com_as_mensagens_decididas()
    {
        var invalido = await CadastrarAsync(Novo("Papelaria Beta", "11.222.333/0001-82"));
        Assert.Equal("CNPJ inválido", Assert.Single(invalido.ValidationErrors).ErrorMessage);

        Assert.Equal(ResultStatus.Created, (await CadastrarAsync(Novo("Papelaria Beta", "11.222.333/0001-81"))).Status);
        var repetido = await CadastrarAsync(Novo("Outra", "11222333000181"));
        Assert.Equal("CNPJ já cadastrado para Papelaria Beta", Assert.Single(repetido.Errors));
    }

    [Theory]
    [InlineData("", "Informe o nome")]
    [InlineData("   ", "Informe o nome")]
    public async Task RN_14_1_nome_obrigatorio(string nome, string mensagem)
    {
        var resultado = await CadastrarAsync(Novo(nome));
        Assert.Equal(mensagem, Assert.Single(resultado.ValidationErrors).ErrorMessage);
    }

    [Fact]
    public async Task RN_14_1_email_com_formato_invalido_e_recusado()
    {
        var resultado = await CadastrarAsync(Novo("João Lima", email: "joao@"));
        Assert.Equal("E-mail inválido", Assert.Single(resultado.ValidationErrors).ErrorMessage);
    }

    [Fact]
    public async Task Alterar_mantendo_o_proprio_cpf_nao_e_repeticao()
    {
        TemMaria();
        var maria = _clientes.Itens[0];
        var resultado = await new AlterarClienteCommandHandler(_clientes).Handle(
            new AlterarClienteCommand(maria.Id, "Maria Souza Lima", "52998224725", null, null, null, null), CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, resultado.Status);
        Assert.Equal("Maria Souza Lima", maria.Nome);
    }

    // ---- Cenário 14.3 / RN-14.4 ----

    [Fact]
    public async Task Cenario_14_3_cliente_com_historico_nao_e_excluido()
    {
        TemMaria();
        var maria = _clientes.Itens[0];
        var resultado = await new ExcluirClienteCommandHandler(_clientes, [new HistoricoFixo(maria.Id)])
            .Handle(new ExcluirClienteCommand(maria.Id), CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, resultado.Status);
        Assert.Equal("Este cliente tem histórico. Você pode inativá-lo", Assert.Single(resultado.Errors));
        Assert.Single(_clientes.Itens);
    }

    [Fact]
    public async Task Cliente_sem_historico_e_excluido()
    {
        TemMaria();
        var resultado = await new ExcluirClienteCommandHandler(_clientes, [new HistoricoFixo()])
            .Handle(new ExcluirClienteCommand(_clientes.Itens[0].Id), CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, resultado.Status);
        Assert.Empty(_clientes.Itens);
    }

    [Fact]
    public async Task Inativar_e_reativar()
    {
        TemMaria();
        var maria = _clientes.Itens[0];
        await new InativarClienteCommandHandler(_clientes).Handle(new InativarClienteCommand(maria.Id), CancellationToken.None);
        Assert.False(maria.Ativo);
        await new ReativarClienteCommandHandler(_clientes).Handle(new ReativarClienteCommand(maria.Id), CancellationToken.None);
        Assert.True(maria.Ativo);
    }

    [Fact]
    public void RN_14_6_comandos_nao_levam_dados_pessoais_para_o_texto_de_log()
    {
        var texto = Novo("Maria Souza", "529.982.247-25", "(31) 99999-0000", "maria@exemplo.com").ToString();
        Assert.DoesNotContain("529", texto);
        Assert.DoesNotContain("99999", texto);
        Assert.DoesNotContain("maria@", texto);
    }

    [Theory]
    [InlineData("maria", new[] { "maria" }, null)]
    [InlineData("  souza   maria ", new[] { "souza", "maria" }, null)]
    [InlineData("529.982.247-25", new string[0], "52998224725")]
    [InlineData("(31) 99999-0000", new string[0], "31999990000")]
    public void RN_14_3_busca_por_nome_ou_por_digitos(string texto, string[] palavras, string? digitos)
    {
        var termos = TermosDeBusca.De(texto);
        Assert.Equal(palavras, termos.Palavras);
        Assert.Equal(digitos, termos.Digitos);
    }
}
