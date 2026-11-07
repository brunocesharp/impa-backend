using Ardalis.Result;
using FluentValidation;
using Impa.Modules.Financeiro.Application.Contas;
using Impa.Modules.Financeiro.Domain;
using MediatR;

namespace Impa.Modules.Financeiro.Application.Categorias.Commands;

public interface INomeDaCategoria
{
    string Nome { get; }
}

public sealed record CriarCategoriaCommand(string Nome) : IRequest<Result<CategoriaDto>>, INomeDaCategoria;

public sealed record AlterarCategoriaCommand(Guid CategoriaId, string Nome) : IRequest<Result<CategoriaDto>>, INomeDaCategoria;

public sealed record InativarCategoriaCommand(Guid CategoriaId) : IRequest<Result>;

public sealed record ReativarCategoriaCommand(Guid CategoriaId) : IRequest<Result>;

/// <summary>RN-23.1: categoria com contas não pode ser excluída, só inativada.</summary>
public sealed record ExcluirCategoriaCommand(Guid CategoriaId) : IRequest<Result>;

internal static class RegrasDaCategoria
{
    public static void Aplicar<T>(AbstractValidator<T> v) where T : INomeDaCategoria =>
        v.RuleFor(c => c.Nome)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Informe o nome da categoria")
            .Must(n => n is null || n.Trim().Length <= CategoriaFinanceira.NomeMaximo)
            .WithMessage($"Use no máximo {CategoriaFinanceira.NomeMaximo} caracteres");

    public static async Task<string?> NomeRepetidoAsync(ICategoriasRepository categorias, string nome, Guid? exceto, CancellationToken ct) =>
        await categorias.ObterPorNomeAsync(nome.Trim(), exceto, ct) is { } outra
            ? MensagensFinanceiro.CategoriaRepetida(outra.Nome)
            : null;
}

public sealed class CriarCategoriaCommandValidator : AbstractValidator<CriarCategoriaCommand>
{
    public CriarCategoriaCommandValidator() => RegrasDaCategoria.Aplicar(this);
}

public sealed class AlterarCategoriaCommandValidator : AbstractValidator<AlterarCategoriaCommand>
{
    public AlterarCategoriaCommandValidator() => RegrasDaCategoria.Aplicar(this);
}

public sealed class CriarCategoriaCommandHandler(ICategoriasRepository categorias, TimeProvider relogio)
    : IRequestHandler<CriarCategoriaCommand, Result<CategoriaDto>>
{
    public async Task<Result<CategoriaDto>> Handle(CriarCategoriaCommand request, CancellationToken ct)
    {
        if (await RegrasDaCategoria.NomeRepetidoAsync(categorias, request.Nome, null, ct) is { } repetida)
            return Result<CategoriaDto>.Conflict(repetida);

        var categoria = CategoriaFinanceira.Criar(request.Nome, relogio.GetUtcNow());
        if (!await categorias.AdicionarAsync(categoria, ct))
            return Result<CategoriaDto>.Conflict(MensagensFinanceiro.CategoriaRepetida(categoria.Nome));

        return Result.Created(CategoriaDto.De(categoria));
    }
}

public sealed class AlterarCategoriaCommandHandler(ICategoriasRepository categorias)
    : IRequestHandler<AlterarCategoriaCommand, Result<CategoriaDto>>
{
    public async Task<Result<CategoriaDto>> Handle(AlterarCategoriaCommand request, CancellationToken ct)
    {
        if (await categorias.ObterAsync(request.CategoriaId, ct) is not { } categoria)
            return Result<CategoriaDto>.NotFound(MensagensFinanceiro.CategoriaNaoEncontrada);

        if (await RegrasDaCategoria.NomeRepetidoAsync(categorias, request.Nome, categoria.Id, ct) is { } repetida)
            return Result<CategoriaDto>.Conflict(repetida);

        categoria.Renomear(request.Nome);
        if (!await categorias.SalvarAsync(ct))
            return Result<CategoriaDto>.Conflict(MensagensFinanceiro.CategoriaRepetida(request.Nome.Trim()));

        return Result.Success(CategoriaDto.De(categoria));
    }
}

public sealed class InativarCategoriaCommandHandler(ICategoriasRepository categorias) : IRequestHandler<InativarCategoriaCommand, Result>
{
    public async Task<Result> Handle(InativarCategoriaCommand request, CancellationToken ct)
    {
        if (await categorias.ObterAsync(request.CategoriaId, ct) is not { } categoria)
            return Result.NotFound(MensagensFinanceiro.CategoriaNaoEncontrada);

        categoria.Inativar();
        await categorias.SalvarAsync(ct);
        return Result.Success();
    }
}

public sealed class ReativarCategoriaCommandHandler(ICategoriasRepository categorias) : IRequestHandler<ReativarCategoriaCommand, Result>
{
    public async Task<Result> Handle(ReativarCategoriaCommand request, CancellationToken ct)
    {
        if (await categorias.ObterAsync(request.CategoriaId, ct) is not { } categoria)
            return Result.NotFound(MensagensFinanceiro.CategoriaNaoEncontrada);

        categoria.Reativar();
        await categorias.SalvarAsync(ct);
        return Result.Success();
    }
}

public sealed class ExcluirCategoriaCommandHandler(ICategoriasRepository categorias, IContasRepository contas)
    : IRequestHandler<ExcluirCategoriaCommand, Result>
{
    public async Task<Result> Handle(ExcluirCategoriaCommand request, CancellationToken ct)
    {
        if (await categorias.ObterAsync(request.CategoriaId, ct) is not { } categoria)
            return Result.NotFound(MensagensFinanceiro.CategoriaNaoEncontrada);

        // A chave estrangeira (ON DELETE RESTRICT) também recusa, se uma conta entrar entre a consulta e a exclusão.
        if (await contas.ExisteDaCategoriaAsync(categoria.Id, ct))
            return Result.Conflict(MensagensFinanceiro.CategoriaComContas);

        await categorias.RemoverAsync(categoria, ct);
        return Result.Success();
    }
}
