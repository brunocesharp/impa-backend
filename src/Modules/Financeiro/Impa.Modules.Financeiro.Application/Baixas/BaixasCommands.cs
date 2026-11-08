using Ardalis.Result;
using FluentValidation;
using Impa.Modules.Financeiro.Application.Categorias;
using Impa.Modules.Financeiro.Application.Contas;
using Impa.Modules.Financeiro.Application.FormasDePagamento;
using Impa.Modules.Financeiro.Domain;
using MediatR;

namespace Impa.Modules.Financeiro.Application.Baixas;

/// <summary>
/// Registra o pagamento total ou parcial de uma conta (RN-23.5, cenário 23.2): data, valor (&gt; 0 e no máximo o saldo)
/// e forma de pagamento financeira ativa. Devolve a conta atualizada.
/// </summary>
public sealed record PagarContaCommand(Guid ContaId, DateOnly? Data, decimal Valor, Guid FormaDePagamentoId)
    : IRequest<Result<ContaDto>>;

/// <summary>
/// Estorna uma baixa com motivo (RN-23.7, cenário 23.5): a baixa fica registrada como estornada (quem, quando e o
/// motivo) e o saldo volta. Devolve a conta atualizada.
/// </summary>
public sealed record EstornarBaixaCommand(Guid BaixaId, string Motivo, Guid UsuarioId, string UsuarioNome)
    : IRequest<Result<ContaDto>>;

public sealed class PagarContaCommandValidator : AbstractValidator<PagarContaCommand>
{
    public PagarContaCommandValidator()
    {
        RuleFor(c => c.ContaId).NotEmpty().WithMessage("Informe a conta");
        RuleFor(c => c.Data)
            .NotNull().WithMessage("Informe a data do pagamento")
            .Must(d => d is null || Conta.VencimentoNaFaixa(d.Value))
            .WithMessage($"Informe uma data entre {Conta.AnoMinimo} e {Conta.AnoMaximo}");
        RuleFor(c => c.Valor)
            .GreaterThan(0).WithMessage("O valor precisa ser maior que zero")
            .LessThanOrEqualTo(Conta.ValorMaximo).WithMessage("O valor pode ser de no máximo R$ 999.999.999.999,99")
            .Must(valor => decimal.Round(valor, 2) == valor).WithMessage("Use no máximo dois decimais no valor");
        RuleFor(c => c.FormaDePagamentoId).NotEmpty().WithMessage("Informe a forma de pagamento");
    }
}

public sealed class EstornarBaixaCommandValidator : AbstractValidator<EstornarBaixaCommand>
{
    public EstornarBaixaCommandValidator()
    {
        RuleFor(c => c.BaixaId).NotEmpty().WithMessage("Informe o pagamento");
        RuleFor(c => c.Motivo)
            .Must(m => !string.IsNullOrWhiteSpace(m)).WithMessage("Informe o motivo do estorno")
            .Must(m => m is null || m.Trim().Length <= Baixa.MotivoMaximo)
            .WithMessage($"Use no máximo {Baixa.MotivoMaximo} caracteres");
    }
}

public sealed class PagarContaCommandHandler(
    IContasRepository contas, ICategoriasRepository categorias, IFormasDePagamentoRepository formas, TimeProvider relogio)
    : IRequestHandler<PagarContaCommand, Result<ContaDto>>
{
    public async Task<Result<ContaDto>> Handle(PagarContaCommand request, CancellationToken ct)
    {
        if (await contas.ObterAsync(request.ContaId, ct) is not { } conta)
            return Result<ContaDto>.NotFound(MensagensFinanceiro.ContaNaoEncontrada);

        var forma = await formas.ObterAsync(request.FormaDePagamentoId, ct);
        var erroDaForma = forma switch
        {
            null => MensagensFinanceiro.FormaDePagamentoNaoEncontrada,
            { Ativa: false } => MensagensFinanceiro.FormaDePagamentoInativa,
            _ => null,
        };
        ValidationError[] erros =
        [
            .. erroDaForma is null ? [] : new[] { new ValidationError(nameof(PagarContaCommand.FormaDePagamentoId), erroDaForma) },
            // RN-23.5 / cenário 23.2: "Valor maior que o saldo da conta (R$ 500,00)".
            .. request.Valor > conta.Saldo
                ? new[] { new ValidationError(nameof(PagarContaCommand.Valor), Conta.ValorMaiorQueOSaldo(conta.Saldo)) }
                : [],
        ];
        if (erros.Length > 0)
            return Result<ContaDto>.Invalid(erros);

        conta.Pagar(request.Data!.Value, request.Valor, forma!.Id, relogio.GetUtcNow());
        if (!await contas.SalvarAsync(ct))
            return Result<ContaDto>.Conflict(MensagensFinanceiro.ContaAlteradaAoMesmoTempo);

        return Result.Created(ContaDto.De(
            conta, await CategoriasDasContas.NomeAsync(categorias, conta.CategoriaId, ct), await NomesDasFormas.ListarAsync(formas, ct)));
    }
}

public sealed class EstornarBaixaCommandHandler(
    IContasRepository contas, ICategoriasRepository categorias, IFormasDePagamentoRepository formas, TimeProvider relogio)
    : IRequestHandler<EstornarBaixaCommand, Result<ContaDto>>
{
    public async Task<Result<ContaDto>> Handle(EstornarBaixaCommand request, CancellationToken ct)
    {
        if (await contas.ObterPelaBaixaAsync(request.BaixaId, ct) is not { } conta)
            return Result<ContaDto>.NotFound(MensagensFinanceiro.PagamentoNaoEncontrado);

        if (conta.Baixas.Single(b => b.Id == request.BaixaId).Estornada)
            return Result<ContaDto>.Conflict(MensagensFinanceiro.PagamentoJaEstornado);

        conta.Estornar(request.BaixaId, request.Motivo, new UsuarioDoEstorno(request.UsuarioId, request.UsuarioNome), relogio.GetUtcNow());
        if (!await contas.SalvarAsync(ct))
            return Result<ContaDto>.Conflict(MensagensFinanceiro.ContaAlteradaAoMesmoTempo);

        return Result.Success(ContaDto.De(
            conta, await CategoriasDasContas.NomeAsync(categorias, conta.CategoriaId, ct), await NomesDasFormas.ListarAsync(formas, ct)));
    }
}
