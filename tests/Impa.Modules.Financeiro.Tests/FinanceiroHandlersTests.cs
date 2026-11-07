using Ardalis.Result;
using FluentValidation;
using Impa.Modules.Financeiro.Application;
using Impa.Modules.Financeiro.Application.Categorias;
using Impa.Modules.Financeiro.Application.Categorias.Commands;
using Impa.Modules.Financeiro.Application.Categorias.Queries;
using Impa.Modules.Financeiro.Application.Contas;
using Impa.Modules.Financeiro.Application.Contas.Commands;
using Impa.Modules.Financeiro.Application.Contas.Queries;
using Impa.Modules.Financeiro.Domain;
using Impa.Modules.Pessoas.Contracts;

namespace Impa.Modules.Financeiro.Tests;

/// <summary>Story 11-1 — categorias (RN-23.1), lançamento (RN-23.2, RN-23.3) e a porta de histórico (RN-14.4).</summary>
public class FinanceiroHandlersTests
{
    private const string CnpjAlfa = "11.222.333/0001-81";

    private readonly CategoriasEmMemoria _categorias = new();
    private readonly ContasEmMemoria _contas = new();
    private readonly PessoasFixas _pessoas = new();
    private readonly FornecedorResumo _alfa = new(Guid.CreateVersion7(), "Distribuidora Alfa", null, "11222333000181", true);

    public FinanceiroHandlersTests() => _pessoas.Fornecedores.Add(_alfa);

    private static async Task<IReadOnlyList<ValidationError>> ValidarAsync<T>(AbstractValidator<T> validator, T comando) =>
        (await validator.ValidateAsync(comando)).Errors.Select(e => new ValidationError(e.PropertyName, e.ErrorMessage)).ToList();

    private async Task<CategoriaDto> CategoriaAsync(string nome)
    {
        var resultado = await new CriarCategoriaCommandHandler(_categorias, TimeProvider.System)
            .Handle(new CriarCategoriaCommand(nome), CancellationToken.None);
        Assert.Equal(ResultStatus.Created, resultado.Status);
        return resultado.Value;
    }

    private static LancarContaCommand Lancamento(Guid categoriaId, decimal valor = 1000m, int parcelas = 3,
        string? fornecedor = CnpjAlfa, string descricao = "Compra de mochilas") =>
        new(descricao, categoriaId, valor, new DateOnly(2027, 1, 31), parcelas, fornecedor, null, null);

    private async Task<Result<IReadOnlyList<ContaDto>>> LancarAsync(LancarContaCommand comando)
    {
        var erros = await ValidarAsync(new LancarContaCommandValidator(), comando);
        if (erros.Count > 0)
            return Result<IReadOnlyList<ContaDto>>.Invalid(erros);
        return await new LancarContaCommandHandler(_contas, _categorias, _pessoas, TimeProvider.System)
            .Handle(comando, CancellationToken.None);
    }

    // ---- Lançamento (CA-1, CA-3, CA-4) ----

    [Fact]
    public async Task Cenario_23_1_lanca_as_tres_parcelas_com_o_fornecedor()
    {
        var categoria = await CategoriaAsync("Compra de mercadorias");

        var resultado = await LancarAsync(Lancamento(categoria.Id));

        Assert.Equal(ResultStatus.Created, resultado.Status);
        Assert.Equal(
            [("1/3", 333.34m, new DateOnly(2027, 1, 31)), ("2/3", 333.33m, new DateOnly(2027, 2, 28)), ("3/3", 333.33m, new DateOnly(2027, 3, 31))],
            resultado.Value.Select(c => ($"{c.Parcela}/{c.TotalDeParcelas}", c.Valor, c.Vencimento)));
        Assert.All(resultado.Value, c =>
        {
            Assert.Equal("Compra de mercadorias", c.Categoria);
            Assert.Equal(new FornecedorDaContaDto(_alfa.Id, "Distribuidora Alfa", CnpjAlfa), c.Fornecedor);
        });
        Assert.Equal(3, _contas.Itens.Count);
    }

    [Fact]
    public async Task Fornecedor_numero_do_documento_e_observacao_sao_opcionais()
    {
        var categoria = await CategoriaAsync("Aluguel");

        var resultado = await LancarAsync(Lancamento(categoria.Id, 2000m, 1, fornecedor: null, descricao: "Aluguel novembro"));

        var conta = Assert.Single(resultado.Value);
        Assert.Null(conta.Fornecedor);
        Assert.Null(conta.NumeroDocumento);
        Assert.Null(conta.ParcelamentoId);
    }

    [Theory]
    [InlineData("", 100, "Descricao", "Informe a descrição")]
    [InlineData("Aluguel", 0, "Valor", "O valor precisa ser maior que zero")]
    [InlineData("Aluguel", -5, "Valor", "O valor precisa ser maior que zero")]
    [InlineData("Aluguel", 10.001, "Valor", "Use no máximo dois decimais no valor")]
    public async Task RN_23_2_descricao_e_valor_positivo_sao_obrigatorios(string descricao, decimal valor, string campo, string mensagem)
    {
        var erros = await ValidarAsync(new LancarContaCommandValidator(), Lancamento(Guid.CreateVersion7(), valor, 1, descricao: descricao));

        var erro = Assert.Single(erros);
        Assert.Equal((campo, mensagem), (erro.Identifier, erro.ErrorMessage));
    }

    [Fact]
    public async Task RN_23_2_categoria_e_vencimento_sao_obrigatorios()
    {
        var comando = Lancamento(Guid.Empty, parcelas: 1) with { Vencimento = null };

        var erros = await ValidarAsync(new LancarContaCommandValidator(), comando);

        Assert.Equal(
            [("CategoriaId", "Informe a categoria"), ("Vencimento", "Informe o vencimento")],
            erros.Select(e => (e.Identifier, e.ErrorMessage)).OrderBy(e => e.Identifier));
    }

    [Theory]
    [InlineData(0, "Informe de 1 a 120 parcelas")]
    [InlineData(121, "Informe de 1 a 120 parcelas")]
    public async Task Quantidade_de_parcelas_entre_1_e_120(int parcelas, string mensagem)
    {
        var erro = Assert.Single(await ValidarAsync(new LancarContaCommandValidator(), Lancamento(Guid.CreateVersion7(), parcelas: parcelas)));

        Assert.Equal(("Parcelas", mensagem), (erro.Identifier, erro.ErrorMessage));
    }

    [Fact]
    public async Task Parcela_de_menos_de_um_centavo_e_recusada()
    {
        var erro = Assert.Single(await ValidarAsync(new LancarContaCommandValidator(), Lancamento(Guid.CreateVersion7(), 0.02m, 3)));

        Assert.Equal(("Parcelas", "Cada parcela precisa ser de pelo menos R$ 0,01"), (erro.Identifier, erro.ErrorMessage));
    }

    [Fact]
    public async Task Documento_do_fornecedor_invalido_e_recusado_pelo_validator()
    {
        var erro = Assert.Single(await ValidarAsync(new LancarContaCommandValidator(), Lancamento(Guid.CreateVersion7(), fornecedor: "11.222.333/0001-82")));

        Assert.Equal(("FornecedorDocumento", "CNPJ inválido"), (erro.Identifier, erro.ErrorMessage));
    }

    [Fact]
    public async Task Fornecedor_que_nao_existe_no_Pessoas_e_recusado()
    {
        var categoria = await CategoriaAsync("Compra de mercadorias");

        var resultado = await LancarAsync(Lancamento(categoria.Id, fornecedor: "529.982.247-25"));

        Assert.Equal(ResultStatus.Invalid, resultado.Status);
        Assert.Equal(("FornecedorDocumento", "Fornecedor não encontrado"),
            (resultado.ValidationErrors.Single().Identifier, resultado.ValidationErrors.Single().ErrorMessage));
        Assert.Empty(_contas.Itens);
    }

    [Fact]
    public async Task Categoria_inexistente_ou_inativa_e_recusada_no_lancamento()
    {
        Assert.Equal("Categoria não encontrada",
            (await LancarAsync(Lancamento(Guid.CreateVersion7()))).ValidationErrors.Single().ErrorMessage);

        var energia = await CategoriaAsync("Energia");
        _categorias.Itens.Single().Inativar();
        var resultado = await LancarAsync(Lancamento(energia.Id));

        Assert.Equal(("CategoriaId", "Categoria inativa"),
            (resultado.ValidationErrors.Single().Identifier, resultado.ValidationErrors.Single().ErrorMessage));
    }

    // ---- Alterar, excluir, listar ----

    [Fact]
    public async Task Alterar_uma_parcela_e_excluir_outra()
    {
        var categoria = await CategoriaAsync("Compra de mercadorias");
        var parcelas = (await LancarAsync(Lancamento(categoria.Id))).Value;

        var alterada = await new AlterarContaCommandHandler(_contas, _categorias, _pessoas).Handle(
            new AlterarContaCommand(parcelas[1].Id, "Mochilas", categoria.Id, 340m, new DateOnly(2027, 3, 1), null, "NF 9", "Combinado"),
            CancellationToken.None);
        Assert.Equal(ResultStatus.Ok, alterada.Status);
        Assert.Equal(("Mochilas", 340m, (FornecedorDaContaDto?)null, 2), (alterada.Value.Descricao, alterada.Value.Valor, alterada.Value.Fornecedor, alterada.Value.Parcela));

        var excluida = await new ExcluirContaCommandHandler(_contas).Handle(new ExcluirContaCommand(parcelas[2].Id), CancellationToken.None);
        Assert.Equal(ResultStatus.Ok, excluida.Status);

        var lista = await new ListarContasQueryHandler(_contas, _categorias).Handle(new ListarContasQuery(), CancellationToken.None);
        Assert.Equal([parcelas[0].Id, parcelas[1].Id], lista.Value.Select(c => c.Id));
    }

    [Fact]
    public async Task Alterar_mantem_a_categoria_atual_mesmo_inativa_mas_nao_aceita_outra_inativa()
    {
        var energia = await CategoriaAsync("Energia");
        var internet = await CategoriaAsync("Internet");
        var conta = Assert.Single((await LancarAsync(Lancamento(energia.Id, 350m, 1, null))).Value);
        _categorias.Itens.ForEach(c => c.Inativar());
        var handler = new AlterarContaCommandHandler(_contas, _categorias, _pessoas);

        var mesma = await handler.Handle(new AlterarContaCommand(conta.Id, "Energia outubro", energia.Id, 350m, conta.Vencimento, null, null, null), CancellationToken.None);
        var outra = await handler.Handle(new AlterarContaCommand(conta.Id, "Energia outubro", internet.Id, 350m, conta.Vencimento, null, null, null), CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, mesma.Status);
        Assert.Equal("Categoria inativa", outra.ValidationErrors.Single().ErrorMessage);
    }

    [Fact]
    public async Task Conta_inexistente_404()
    {
        Assert.Equal(ResultStatus.NotFound, (await new ExcluirContaCommandHandler(_contas)
            .Handle(new ExcluirContaCommand(Guid.CreateVersion7()), CancellationToken.None)).Status);
        Assert.Equal(ResultStatus.NotFound, (await new ObterContaQueryHandler(_contas, _categorias)
            .Handle(new ObterContaQuery(Guid.CreateVersion7()), CancellationToken.None)).Status);
    }

    // ---- Categorias (CA-2) ----

    [Fact]
    public async Task RN_23_1_categoria_com_contas_nao_e_excluida_so_inativada()
    {
        var aluguel = await CategoriaAsync("Aluguel");
        var energia = await CategoriaAsync("Energia");
        await LancarAsync(Lancamento(aluguel.Id, 2000m, 1, null, "Aluguel novembro"));
        var excluir = new ExcluirCategoriaCommandHandler(_categorias, _contas);

        var comContas = await excluir.Handle(new ExcluirCategoriaCommand(aluguel.Id), CancellationToken.None);
        Assert.Equal(ResultStatus.Conflict, comContas.Status);
        Assert.Equal("Esta categoria tem contas. Você pode inativá-la", Assert.Single(comContas.Errors));

        Assert.Equal(ResultStatus.Ok, (await new InativarCategoriaCommandHandler(_categorias)
            .Handle(new InativarCategoriaCommand(aluguel.Id), CancellationToken.None)).Status);
        Assert.Equal(ResultStatus.Ok, (await excluir.Handle(new ExcluirCategoriaCommand(energia.Id), CancellationToken.None)).Status);

        var ativas = await new ListarCategoriasQueryHandler(_categorias).Handle(new ListarCategoriasQuery(false), CancellationToken.None);
        var todas = await new ListarCategoriasQueryHandler(_categorias).Handle(new ListarCategoriasQuery(true), CancellationToken.None);
        Assert.Empty(ativas.Value);
        Assert.Equal([new CategoriaDto(aluguel.Id, "Aluguel", false)], todas.Value);

        Assert.Equal(ResultStatus.Ok, (await new ReativarCategoriaCommandHandler(_categorias)
            .Handle(new ReativarCategoriaCommand(aluguel.Id), CancellationToken.None)).Status);
        Assert.True(_categorias.Itens.Single().Ativa);
    }

    [Fact]
    public async Task Nome_da_categoria_obrigatorio_e_sem_repetir()
    {
        Assert.Equal("Informe o nome da categoria",
            Assert.Single(await ValidarAsync(new CriarCategoriaCommandValidator(), new CriarCategoriaCommand(" "))).ErrorMessage);

        var energia = await CategoriaAsync("Energia");
        var internet = await CategoriaAsync("Internet");
        var repetida = await new CriarCategoriaCommandHandler(_categorias, TimeProvider.System)
            .Handle(new CriarCategoriaCommand(" energia "), CancellationToken.None);
        Assert.Equal(ResultStatus.Conflict, repetida.Status);
        Assert.Equal("Já existe a categoria Energia", Assert.Single(repetida.Errors));

        var renomear = new AlterarCategoriaCommandHandler(_categorias);
        Assert.Equal(ResultStatus.Conflict, (await renomear.Handle(new AlterarCategoriaCommand(internet.Id, "ENERGIA"), CancellationToken.None)).Status);
        var renomeada = await renomear.Handle(new AlterarCategoriaCommand(internet.Id, "Internet e telefone"), CancellationToken.None);
        Assert.Equal("Internet e telefone", renomeada.Value.Nome);
        Assert.Equal(ResultStatus.Ok, (await renomear.Handle(new AlterarCategoriaCommand(energia.Id, "energia"), CancellationToken.None)).Status);
    }

    // ---- Porta IHistoricoDaPessoa (RN-14.4) ----

    [Fact]
    public async Task Fornecedor_com_conta_tem_historico_no_financeiro()
    {
        var historico = new HistoricoDaPessoaNoFinanceiro(_contas);
        var categoria = await CategoriaAsync("Compra de mercadorias");

        Assert.False(await historico.TemHistoricoAsync(TipoDePessoa.Fornecedor, _alfa.Id, CancellationToken.None));

        await LancarAsync(Lancamento(categoria.Id));

        Assert.True(await historico.TemHistoricoAsync(TipoDePessoa.Fornecedor, _alfa.Id, CancellationToken.None));
        Assert.False(await historico.TemHistoricoAsync(TipoDePessoa.Cliente, _alfa.Id, CancellationToken.None));
    }
}
