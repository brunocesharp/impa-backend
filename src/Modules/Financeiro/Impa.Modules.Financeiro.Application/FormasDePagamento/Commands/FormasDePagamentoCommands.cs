using Ardalis.Result;
using FluentValidation;
using Impa.Modules.Financeiro.Application.Contas;
using Impa.Modules.Financeiro.Domain;
using MediatR;

namespace Impa.Modules.Financeiro.Application.FormasDePagamento.Commands;

public interface INomeDaFormaDePagamento
{
    string Nome { get; }
}

public sealed record CriarFormaDePagamentoCommand(string Nome) : IRequest<Result<FormaDePagamentoDto>>, INomeDaFormaDePagamento;

public sealed record AlterarFormaDePagamentoCommand(Guid FormaDePagamentoId, string Nome)
    : IRequest<Result<FormaDePagamentoDto>>, INomeDaFormaDePagamento;

public sealed record InativarFormaDePagamentoCommand(Guid FormaDePagamentoId) : IRequest<Result>;

public sealed record ReativarFormaDePagamentoCommand(Guid FormaDePagamentoId) : IRequest<Result>;

/// <summary>Forma com pagamentos não pode ser excluída, só inativada (a baixa guarda a forma usada).</summary>
public sealed record ExcluirFormaDePagamentoCommand(Guid FormaDePagamentoId) : IRequest<Result>;

internal static class RegrasDaFormaDePagamento
{
    public static void Aplicar<T>(AbstractValidator<T> v) where T : INomeDaFormaDePagamento =>
        v.RuleFor(c => c.Nome)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Informe o nome da forma de pagamento")
            .Must(n => n is null || n.Trim().Length <= FormaDePagamentoFinanceira.NomeMaximo)
            .WithMessage($"Use no máximo {FormaDePagamentoFinanceira.NomeMaximo} caracteres");

    public static async Task<string?> NomeRepetidoAsync(IFormasDePagamentoRepository formas, string nome, Guid? exceto, CancellationToken ct) =>
        await formas.ObterPorNomeAsync(nome.Trim(), exceto, ct) is { } outra
            ? MensagensFinanceiro.FormaDePagamentoRepetida(outra.Nome)
            : null;
}

public sealed class CriarFormaDePagamentoCommandValidator : AbstractValidator<CriarFormaDePagamentoCommand>
{
    public CriarFormaDePagamentoCommandValidator() => RegrasDaFormaDePagamento.Aplicar(this);
}

public sealed class AlterarFormaDePagamentoCommandValidator : AbstractValidator<AlterarFormaDePagamentoCommand>
{
    public AlterarFormaDePagamentoCommandValidator() => RegrasDaFormaDePagamento.Aplicar(this);
}

public sealed class CriarFormaDePagamentoCommandHandler(IFormasDePagamentoRepository formas, TimeProvider relogio)
    : IRequestHandler<CriarFormaDePagamentoCommand, Result<FormaDePagamentoDto>>
{
    public async Task<Result<FormaDePagamentoDto>> Handle(CriarFormaDePagamentoCommand request, CancellationToken ct)
    {
        if (await RegrasDaFormaDePagamento.NomeRepetidoAsync(formas, request.Nome, null, ct) is { } repetida)
            return Result<FormaDePagamentoDto>.Conflict(repetida);

        var forma = FormaDePagamentoFinanceira.Criar(request.Nome, relogio.GetUtcNow());
        if (!await formas.AdicionarAsync(forma, ct))
            return Result<FormaDePagamentoDto>.Conflict(MensagensFinanceiro.FormaDePagamentoRepetida(forma.Nome));

        return Result.Created(FormaDePagamentoDto.De(forma));
    }
}

public sealed class AlterarFormaDePagamentoCommandHandler(IFormasDePagamentoRepository formas)
    : IRequestHandler<AlterarFormaDePagamentoCommand, Result<FormaDePagamentoDto>>
{
    public async Task<Result<FormaDePagamentoDto>> Handle(AlterarFormaDePagamentoCommand request, CancellationToken ct)
    {
        if (await formas.ObterAsync(request.FormaDePagamentoId, ct) is not { } forma)
            return Result<FormaDePagamentoDto>.NotFound(MensagensFinanceiro.FormaDePagamentoNaoEncontrada);

        if (await RegrasDaFormaDePagamento.NomeRepetidoAsync(formas, request.Nome, forma.Id, ct) is { } repetida)
            return Result<FormaDePagamentoDto>.Conflict(repetida);

        forma.Renomear(request.Nome);
        if (!await formas.SalvarAsync(ct))
            return Result<FormaDePagamentoDto>.Conflict(MensagensFinanceiro.FormaDePagamentoRepetida(request.Nome.Trim()));

        return Result.Success(FormaDePagamentoDto.De(forma));
    }
}

public sealed class InativarFormaDePagamentoCommandHandler(IFormasDePagamentoRepository formas)
    : IRequestHandler<InativarFormaDePagamentoCommand, Result>
{
    public async Task<Result> Handle(InativarFormaDePagamentoCommand request, CancellationToken ct)
    {
        if (await formas.ObterAsync(request.FormaDePagamentoId, ct) is not { } forma)
            return Result.NotFound(MensagensFinanceiro.FormaDePagamentoNaoEncontrada);

        forma.Inativar();
        await formas.SalvarAsync(ct);
        return Result.Success();
    }
}

public sealed class ReativarFormaDePagamentoCommandHandler(IFormasDePagamentoRepository formas)
    : IRequestHandler<ReativarFormaDePagamentoCommand, Result>
{
    public async Task<Result> Handle(ReativarFormaDePagamentoCommand request, CancellationToken ct)
    {
        if (await formas.ObterAsync(request.FormaDePagamentoId, ct) is not { } forma)
            return Result.NotFound(MensagensFinanceiro.FormaDePagamentoNaoEncontrada);

        forma.Reativar();
        await formas.SalvarAsync(ct);
        return Result.Success();
    }
}

public sealed class ExcluirFormaDePagamentoCommandHandler(IFormasDePagamentoRepository formas, IContasRepository contas)
    : IRequestHandler<ExcluirFormaDePagamentoCommand, Result>
{
    public async Task<Result> Handle(ExcluirFormaDePagamentoCommand request, CancellationToken ct)
    {
        if (await formas.ObterAsync(request.FormaDePagamentoId, ct) is not { } forma)
            return Result.NotFound(MensagensFinanceiro.FormaDePagamentoNaoEncontrada);

        // A chave estrangeira das baixas (ON DELETE RESTRICT) também recusa numa corrida.
        if (await contas.ExisteBaixaDaFormaAsync(forma.Id, ct) || !await formas.RemoverAsync(forma, ct))
            return Result.Conflict(MensagensFinanceiro.FormaDePagamentoComPagamentos);

        return Result.Success();
    }
}
