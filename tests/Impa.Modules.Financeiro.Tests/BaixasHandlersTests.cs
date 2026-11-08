using Ardalis.Result;
using FluentValidation;
using Impa.Modules.Financeiro.Application.Baixas;
using Impa.Modules.Financeiro.Application.Contas;
using Impa.Modules.Financeiro.Application.Contas.Commands;
using Impa.Modules.Financeiro.Application.FormasDePagamento;
using Impa.Modules.Financeiro.Domain;

namespace Impa.Modules.Financeiro.Tests;

/// <summary>Story 11-2 — formas de pagamento (RN-23.4), baixa (RN-23.5), estorno e trava da conta paga (RN-23.7).</summary>
public class BaixasHandlersTests
{
    private const string CnpjAlfa = "11.222.333/0001-81";
    private static readonly DateOnly Dia5 = new(2026, 11, 5);

    private readonly CategoriasEmMemoria _categorias = new();
    private readonly ContasEmMemoria _contas = new();
    private readonly FormasEmMemoria _formas = new();
    private readonly PessoasFixas _pessoas = new();
    private readonly CategoriaFinanceira _aluguel = CategoriaFinanceira.Criar("Aluguel", DateTimeOffset.UtcNow);
    private readonly FormaDePagamentoFinanceira _pix = FormaDePagamentoFinanceira.Criar("Pix", DateTimeOffset.UtcNow);

    public BaixasHandlersTests()
    {
        _categorias.Itens.Add(_aluguel);
        _formas.Itens.Add(_pix);
        _pessoas.Fornecedores.Add(new(Guid.CreateVersion7(), "Imobiliária Alfa", null, "11222333000181", true));
    }

    private static async Task<IReadOnlyList<ValidationError>> ValidarAsync<T>(AbstractValidator<T> validator, T comando) =>
        (await validator.ValidateAsync(comando)).Errors.Select(e => new ValidationError(e.PropertyName, e.ErrorMessage)).ToList();

    private Conta ContaDe(decimal valor, string descricao = "Aluguel novembro", string? fornecedor = null)
    {
        var forn = fornecedor is null ? null : new FornecedorDaConta(_pessoas.Fornecedores[0].Id, "Imobiliária Alfa", "11222333000181");
        var conta = Assert.Single(Conta.Lancar(
            new DadosDaConta(descricao, _aluguel.Id, valor, new DateOnly(2026, 11, 10), forn, "Recibo 11", null), 1, DateTimeOffset.UtcNow));
        _contas.Itens.Add(conta);
        return conta;
    }

    private PagarContaCommandHandler Pagar() => new(_contas, _categorias, _formas, TimeProvider.System);

    private EstornarBaixaCommandHandler Estornar() => new(_contas, _categorias, _formas, TimeProvider.System);

    private Task<Result<ContaDto>> PagarAsync(Guid contaId, decimal valor) =>
        Pagar().Handle(new PagarContaCommand(contaId, Dia5, valor, _pix.Id), CancellationToken.None);

    // ---- Cenário 23.2 (CA-1, CA-4) ----

    [Theory]
    [InlineData(500, "paga", 0)]
    [InlineData(200, "parcialmente-paga", 300)]
    public async Task Cenario_23_2_pagar_total_ou_parcialmente(decimal valor, string situacao, decimal saldo)
    {
        var conta = ContaDe(500m);

        var resultado = await PagarAsync(conta.Id, valor);

        Assert.Equal(ResultStatus.Created, resultado.Status);
        Assert.Equal((situacao, saldo, valor), (resultado.Value.SituacaoDoPagamento, resultado.Value.Saldo, resultado.Value.ValorPago));
        var baixa = Assert.Single(resultado.Value.Baixas);
        Assert.Equal((Dia5, valor, _pix.Id, "Pix", false), (baixa.Data, baixa.Valor, baixa.FormaDePagamentoId, baixa.FormaDePagamento, baixa.Estornada));
    }

    [Fact]
    public async Task Cenario_23_2_pagar_600_e_recusado_com_a_mensagem_do_saldo()
    {
        var conta = ContaDe(500m);

        var resultado = await PagarAsync(conta.Id, 600m);

        Assert.Equal(ResultStatus.Invalid, resultado.Status);
        var erro = Assert.Single(resultado.ValidationErrors);
        Assert.Equal(("Valor", "Valor maior que o saldo da conta (R$ 500,00)"), (erro.Identifier, erro.ErrorMessage));
        Assert.Empty(conta.Baixas);
    }

    [Fact]
    public async Task Forma_inexistente_ou_inativa_e_recusada()
    {
        var conta = ContaDe(500m);
        var inexistente = await Pagar().Handle(new PagarContaCommand(conta.Id, Dia5, 10m, Guid.CreateVersion7()), CancellationToken.None);
        _pix.Inativar();
        var inativa = await PagarAsync(conta.Id, 10m);

        Assert.Equal(("FormaDePagamentoId", "Forma de pagamento não encontrada"),
            (inexistente.ValidationErrors.Single().Identifier, inexistente.ValidationErrors.Single().ErrorMessage));
        Assert.Equal("Forma de pagamento inativa", inativa.ValidationErrors.Single().ErrorMessage);
        Assert.Empty(conta.Baixas);
    }

    [Fact]
    public async Task Conta_inexistente_404_e_gravacao_concorrente_409()
    {
        Assert.Equal(ResultStatus.NotFound, (await PagarAsync(Guid.CreateVersion7(), 10m)).Status);

        var conta = ContaDe(500m);
        _contas.RecusarGravacao = true;
        var resultado = await PagarAsync(conta.Id, 10m);
        Assert.Equal(ResultStatus.Conflict, resultado.Status);
        Assert.Equal("A conta foi alterada em outra operação. Atualize e tente de novo", Assert.Single(resultado.Errors));
    }

    [Theory]
    [InlineData(0, "Valor", "O valor precisa ser maior que zero")]
    [InlineData(-10, "Valor", "O valor precisa ser maior que zero")]
    [InlineData(10.001, "Valor", "Use no máximo dois decimais no valor")]
    public async Task RN_23_5_valor_da_baixa_positivo(decimal valor, string campo, string mensagem)
    {
        var erro = Assert.Single(await ValidarAsync(new PagarContaCommandValidator(), new PagarContaCommand(Guid.CreateVersion7(), Dia5, valor, _pix.Id)));

        Assert.Equal((campo, mensagem), (erro.Identifier, erro.ErrorMessage));
    }

    [Fact]
    public async Task RN_23_5_data_e_forma_sao_obrigatorias()
    {
        var erros = await ValidarAsync(new PagarContaCommandValidator(), new PagarContaCommand(Guid.CreateVersion7(), null, 10m, Guid.Empty));

        Assert.Equal(
            [("Data", "Informe a data do pagamento"), ("FormaDePagamentoId", "Informe a forma de pagamento")],
            erros.Select(e => (e.Identifier, e.ErrorMessage)).OrderBy(e => e.Identifier));
    }

    // ---- Cenário 23.5 (CA-2, CA-5) ----

    [Fact]
    public async Task Cenario_23_5_estornar_uma_baixa()
    {
        var conta = ContaDe(350m, "Energia outubro");
        var baixa = (await PagarAsync(conta.Id, 350m)).Value.Baixas.Single();
        var usuario = Guid.CreateVersion7();

        var resultado = await Estornar().Handle(
            new EstornarBaixaCommand(baixa.Id, "Pagamento lançado na conta errada", usuario, "admin"), CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, resultado.Status);
        Assert.Equal((350m, 0m, "sem-pagamento"), (resultado.Value.Saldo, resultado.Value.ValorPago, resultado.Value.SituacaoDoPagamento));
        var estornada = Assert.Single(resultado.Value.Baixas);
        Assert.True(estornada.Estornada);
        Assert.NotNull(estornada.EstornadaEm);
        Assert.Equal(("admin", "Pagamento lançado na conta errada"), (estornada.EstornadaPor, estornada.MotivoDoEstorno));
        Assert.Equal(usuario, conta.Baixas.Single().EstornadaPorId);
    }

    [Fact]
    public async Task Estorno_exige_motivo_nao_se_repete_e_baixa_inexistente_404()
    {
        var conta = ContaDe(500m);
        var baixa = (await PagarAsync(conta.Id, 200m)).Value.Baixas.Single();

        var semMotivo = Assert.Single(await ValidarAsync(new EstornarBaixaCommandValidator(), new EstornarBaixaCommand(baixa.Id, " ", Guid.CreateVersion7(), "admin")));
        Assert.Equal(("Motivo", "Informe o motivo do estorno"), (semMotivo.Identifier, semMotivo.ErrorMessage));

        var comando = new EstornarBaixaCommand(baixa.Id, "Duplicado", Guid.CreateVersion7(), "admin");
        Assert.Equal(ResultStatus.Ok, (await Estornar().Handle(comando, CancellationToken.None)).Status);
        var deNovo = await Estornar().Handle(comando, CancellationToken.None);
        Assert.Equal(ResultStatus.Conflict, deNovo.Status);
        Assert.Equal("Este pagamento já foi estornado", Assert.Single(deNovo.Errors));

        var inexistente = await Estornar().Handle(comando with { BaixaId = Guid.CreateVersion7() }, CancellationToken.None);
        Assert.Equal(ResultStatus.NotFound, inexistente.Status);
        Assert.Equal("Pagamento não encontrado", Assert.Single(inexistente.Errors));
        Assert.Equal(500m, conta.Saldo);
    }

    // ---- RN-23.7 (CA-5) ----

    [Fact]
    public async Task RN_23_7_conta_com_pagamento_so_altera_descricao_e_observacao()
    {
        var conta = ContaDe(500m, fornecedor: CnpjAlfa);
        await PagarAsync(conta.Id, 200m);
        var handler = new AlterarContaCommandHandler(_contas, _categorias, _formas, _pessoas);
        var igual = new AlterarContaCommand(conta.Id, "Aluguel nov/26", _aluguel.Id, 500m, conta.Vencimento, CnpjAlfa, " Recibo 11 ", "Pago em parte");

        var permitido = await handler.Handle(igual, CancellationToken.None);
        Assert.Equal(ResultStatus.Ok, permitido.Status);
        Assert.Equal(("Aluguel nov/26", "Pago em parte", "parcialmente-paga"),
            (permitido.Value.Descricao, permitido.Value.Observacao, permitido.Value.SituacaoDoPagamento));
        Assert.Equal("Pix", permitido.Value.Baixas.Single().FormaDePagamento);

        var travado = await handler.Handle(igual with
        {
            CategoriaId = Guid.CreateVersion7(), Valor = 600m, Vencimento = new DateOnly(2026, 11, 20),
            FornecedorDocumento = null, NumeroDocumento = "Recibo 12",
        }, CancellationToken.None);
        Assert.Equal(ResultStatus.Invalid, travado.Status);
        Assert.Equal(["CategoriaId", "FornecedorDocumento", "NumeroDocumento", "Valor", "Vencimento"],
            travado.ValidationErrors.Select(e => e.Identifier).Order());
        Assert.All(travado.ValidationErrors, e =>
            Assert.Equal("Conta com pagamento: só a descrição e a observação podem ser alteradas", e.ErrorMessage));
        Assert.Equal((500m, "Aluguel nov/26"), (conta.Valor, conta.Descricao));
    }

    [Fact]
    public async Task RN_23_7_conta_com_pagamento_nao_e_excluida_e_depois_do_estorno_e()
    {
        var conta = ContaDe(500m);
        var baixa = (await PagarAsync(conta.Id, 500m)).Value.Baixas.Single();
        var excluir = new ExcluirContaCommandHandler(_contas);

        var recusada = await excluir.Handle(new ExcluirContaCommand(conta.Id), CancellationToken.None);
        Assert.Equal(ResultStatus.Conflict, recusada.Status);
        Assert.Equal("Conta com pagamento não pode ser excluída. Estorne os pagamentos antes", Assert.Single(recusada.Errors));
        Assert.Contains(conta, _contas.Itens);

        await Estornar().Handle(new EstornarBaixaCommand(baixa.Id, "Errado", Guid.CreateVersion7(), "admin"), CancellationToken.None);
        Assert.Equal(ResultStatus.Ok, (await excluir.Handle(new ExcluirContaCommand(conta.Id), CancellationToken.None)).Status);
        Assert.Empty(_contas.Itens);
    }

    // ---- Formas de pagamento (CA-3) ----

    [Fact]
    public async Task RN_23_4_formas_de_pagamento_cadastro_sem_repetir_e_exclusao_so_sem_pagamentos()
    {
        var criar = new CriarFormaDePagamentoCommandHandler(_formas, TimeProvider.System);
        var boleto = (await criar.Handle(new CriarFormaDePagamentoCommand(" Boleto "), CancellationToken.None)).Value;
        Assert.Equal(new FormaDePagamentoDto(boleto.Id, "Boleto", true), boleto);

        var repetida = await criar.Handle(new CriarFormaDePagamentoCommand("pix"), CancellationToken.None);
        Assert.Equal(ResultStatus.Conflict, repetida.Status);
        Assert.Equal("Já existe a forma de pagamento Pix", Assert.Single(repetida.Errors));
        Assert.Equal("Informe o nome da forma de pagamento",
            Assert.Single(await ValidarAsync(new CriarFormaDePagamentoCommandValidator(), new CriarFormaDePagamentoCommand(""))).ErrorMessage);

        await PagarAsync(ContaDe(100m).Id, 100m);
        var excluir = new ExcluirFormaDePagamentoCommandHandler(_formas, _contas);
        var comPagamentos = await excluir.Handle(new ExcluirFormaDePagamentoCommand(_pix.Id), CancellationToken.None);
        Assert.Equal(ResultStatus.Conflict, comPagamentos.Status);
        Assert.Equal("Esta forma de pagamento tem pagamentos. Você pode inativá-la", Assert.Single(comPagamentos.Errors));

        Assert.Equal(ResultStatus.Ok, (await new InativarFormaDePagamentoCommandHandler(_formas)
            .Handle(new InativarFormaDePagamentoCommand(_pix.Id), CancellationToken.None)).Status);
        var ativas = await new ListarFormasDePagamentoQueryHandler(_formas).Handle(new ListarFormasDePagamentoQuery(), CancellationToken.None);
        Assert.Equal(["Boleto"], ativas.Value.Select(f => f.Nome));
        Assert.Equal(ResultStatus.Ok, (await new ReativarFormaDePagamentoCommandHandler(_formas)
            .Handle(new ReativarFormaDePagamentoCommand(_pix.Id), CancellationToken.None)).Status);

        var renomeada = await new AlterarFormaDePagamentoCommandHandler(_formas)
            .Handle(new AlterarFormaDePagamentoCommand(boleto.Id, "Boleto bancário"), CancellationToken.None);
        Assert.Equal("Boleto bancário", renomeada.Value.Nome);
        Assert.Equal(ResultStatus.Ok, (await excluir.Handle(new ExcluirFormaDePagamentoCommand(boleto.Id), CancellationToken.None)).Status);
        Assert.Equal(ResultStatus.NotFound, (await excluir.Handle(new ExcluirFormaDePagamentoCommand(boleto.Id), CancellationToken.None)).Status);
    }
}
