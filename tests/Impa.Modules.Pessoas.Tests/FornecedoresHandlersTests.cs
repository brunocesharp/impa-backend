using Ardalis.Result;
using Impa.Modules.Pessoas.Application.Fornecedores;
using Impa.Modules.Pessoas.Application.Fornecedores.Commands;

namespace Impa.Modules.Pessoas.Tests;

/// <summary>Story 3-7 — regras do fornecedor (RN-14.2) e RN-14.4.</summary>
public class FornecedoresHandlersTests
{
    private readonly FornecedoresEmMemoria _fornecedores = new();

    private async Task<Result<FornecedorDto>> CadastrarAsync(string razao, string? documento)
    {
        var comando = new CadastrarFornecedorCommand(razao, documento, null, null, null, null);
        var validacao = await new CadastrarFornecedorCommandValidator().ValidateAsync(comando);
        if (!validacao.IsValid)
            return Result.Invalid(validacao.Errors.Select(e => new ValidationError(e.PropertyName, e.ErrorMessage)));
        return await new CadastrarFornecedorCommandHandler(_fornecedores, TimeProvider.System).Handle(comando, CancellationToken.None);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Cenario_14_2_fornecedor_sem_documento_e_recusado(string? documento)
    {
        var resultado = await CadastrarAsync("Distribuidora Alfa", documento);

        Assert.Equal(ResultStatus.Invalid, resultado.Status);
        Assert.Equal("Informe o CNPJ ou CPF do fornecedor", Assert.Single(resultado.ValidationErrors).ErrorMessage);
    }

    [Fact]
    public async Task Fornecedor_com_cnpj_valido_e_salvo()
    {
        var resultado = await CadastrarAsync("Distribuidora Alfa", "11.222.333/0001-81");

        Assert.Equal(ResultStatus.Created, resultado.Status);
        Assert.Equal("11.222.333/0001-81", resultado.Value.Documento);
    }

    [Fact]
    public async Task Fornecedor_pessoa_fisica_com_cpf()
    {
        Assert.Equal(ResultStatus.Created, (await CadastrarAsync("José Artesão", "529.982.247-25")).Status);
    }

    [Fact]
    public async Task CNPJ_invalido_e_repetido_com_as_mensagens_decididas()
    {
        Assert.Equal("CNPJ inválido", Assert.Single((await CadastrarAsync("Alfa", "11.222.333/0001-82")).ValidationErrors).ErrorMessage);

        await CadastrarAsync("Distribuidora Alfa", "11.222.333/0001-81");
        var repetido = await CadastrarAsync("Beta", "11222333000181");
        Assert.Equal(ResultStatus.Conflict, repetido.Status);
        Assert.Equal("CNPJ já cadastrado para o fornecedor Distribuidora Alfa", Assert.Single(repetido.Errors));
    }

    [Fact]
    public async Task Razao_social_obrigatoria()
    {
        Assert.Equal("Informe a razão social",
            Assert.Single((await CadastrarAsync("", "11.222.333/0001-81")).ValidationErrors).ErrorMessage);
    }

    [Fact]
    public async Task RN_14_4_fornecedor_com_historico_nao_e_excluido()
    {
        var criado = await CadastrarAsync("Distribuidora Alfa", "11.222.333/0001-81");
        var resultado = await new ExcluirFornecedorCommandHandler(_fornecedores, [new HistoricoFixo(criado.Value.Id)])
            .Handle(new ExcluirFornecedorCommand(criado.Value.Id), CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, resultado.Status);
        Assert.Equal("Este fornecedor tem histórico. Você pode inativá-lo", Assert.Single(resultado.Errors));
        Assert.Single(_fornecedores.Itens);
    }
}
