using Ardalis.Result;
using FluentValidation;
using Impa.Modules.Financeiro.Application.Categorias;
using Impa.Modules.Financeiro.Application.FormasDePagamento;
using Impa.Modules.Financeiro.Domain;
using Impa.Modules.Pessoas.Contracts;
using Impa.Shared;
using MediatR;

namespace Impa.Modules.Financeiro.Application.Contas.Commands;

/// <summary>Campos do formulário da conta (RN-23.2). <c>FornecedorDocumento</c>: CNPJ/CPF do fornecedor, opcional.</summary>
public interface IDadosDaContaInformados
{
    string Descricao { get; }
    Guid CategoriaId { get; }
    decimal Valor { get; }
    DateOnly? Vencimento { get; }
    string? FornecedorDocumento { get; }
    string? NumeroDocumento { get; }
    string? Observacao { get; }
}

/// <summary>
/// Lança a conta em <paramref name="Parcelas"/> parcelas (RN-23.3, cenário 23.1): <paramref name="Valor"/> é o total e
/// <paramref name="Vencimento"/> o primeiro vencimento. Devolve as contas criadas, uma por parcela.
/// </summary>
public sealed record LancarContaCommand(
    string Descricao,
    Guid CategoriaId,
    decimal Valor,
    DateOnly? Vencimento,
    int Parcelas,
    string? FornecedorDocumento,
    string? NumeroDocumento,
    string? Observacao) : IRequest<Result<IReadOnlyList<ContaDto>>>, IDadosDaContaInformados;

/// <summary>Altera uma parcela. Com pagamento, só descrição e observação podem mudar (RN-23.7).</summary>
public sealed record AlterarContaCommand(
    Guid ContaId,
    string Descricao,
    Guid CategoriaId,
    decimal Valor,
    DateOnly? Vencimento,
    string? FornecedorDocumento,
    string? NumeroDocumento,
    string? Observacao) : IRequest<Result<ContaDto>>, IDadosDaContaInformados;

/// <summary>Exclui uma parcela; com pagamento, é recusado (RN-23.7).</summary>
public sealed record ExcluirContaCommand(Guid ContaId) : IRequest<Result>;

internal static class RegrasDaConta
{
    public static void Aplicar<T>(AbstractValidator<T> v) where T : IDadosDaContaInformados
    {
        v.RuleFor(c => c.Descricao)
            .Must(d => !string.IsNullOrWhiteSpace(d)).WithMessage("Informe a descrição")
            .Must(d => d is null || d.Trim().Length <= Conta.DescricaoMaximo)
            .WithMessage($"Use no máximo {Conta.DescricaoMaximo} caracteres");
        v.RuleFor(c => c.CategoriaId).NotEmpty().WithMessage("Informe a categoria");
        v.RuleFor(c => c.Valor)
            .GreaterThan(0).WithMessage("O valor precisa ser maior que zero")
            .LessThanOrEqualTo(Conta.ValorMaximo).WithMessage("O valor pode ser de no máximo R$ 999.999.999.999,99")
            .Must(valor => decimal.Round(valor, 2) == valor).WithMessage("Use no máximo dois decimais no valor");
        v.RuleFor(c => c.Vencimento)
            .NotNull().WithMessage("Informe o vencimento")
            .Must(d => d is null || Conta.VencimentoNaFaixa(d.Value))
            .WithMessage($"Informe um vencimento entre {Conta.AnoMinimo} e {Conta.AnoMaximo}");
        v.RuleFor(c => c.FornecedorDocumento)
            .Must(d => Documento.EstaVazio(d) || Documento.Erro(d) is null)
            .WithMessage((_, d) => Documento.Erro(d));
        v.RuleFor(c => c.NumeroDocumento)
            .Must(t => string.IsNullOrWhiteSpace(t) || t.Trim().Length <= Conta.NumeroDocumentoMaximo)
            .WithMessage($"Use no máximo {Conta.NumeroDocumentoMaximo} caracteres");
        v.RuleFor(c => c.Observacao)
            .Must(t => string.IsNullOrWhiteSpace(t) || t.Trim().Length <= Conta.ObservacaoMaximo)
            .WithMessage($"Use no máximo {Conta.ObservacaoMaximo} caracteres");
    }

    /// <summary>
    /// Fornecedor pelo contrato do módulo Pessoas (story 3.7). Sem documento: conta sem fornecedor.
    /// Documento que não é de nenhum fornecedor: erro no campo <c>FornecedorDocumento</c>.
    /// </summary>
    public static async Task<(FornecedorDaConta? Fornecedor, ValidationError? Erro)> FornecedorAsync(
        IPessoasConsultas pessoas, string? documento, CancellationToken ct)
    {
        if (Documento.EstaVazio(documento))
            return (null, null);

        return await pessoas.ObterFornecedorPorDocumentoAsync(documento!, ct) is { } f
            ? (new FornecedorDaConta(f.Id, f.RazaoSocial, f.Documento), null)
            : (null, new ValidationError(nameof(IDadosDaContaInformados.FornecedorDocumento), MensagensFinanceiro.FornecedorNaoEncontrado));
    }

    /// <summary>A categoria precisa existir e estar ativa; <paramref name="atual"/> (a da conta alterada) vale mesmo inativa.</summary>
    public static async Task<(CategoriaFinanceira? Categoria, ValidationError? Erro)> CategoriaAsync(
        ICategoriasRepository categorias, Guid id, Guid? atual, CancellationToken ct)
    {
        var categoria = await categorias.ObterAsync(id, ct);
        var erro = categoria switch
        {
            null => MensagensFinanceiro.CategoriaNaoEncontrada,
            { Ativa: false } when categoria.Id != atual => MensagensFinanceiro.CategoriaInativa,
            _ => null,
        };
        return (categoria, erro is null ? null : new ValidationError(nameof(IDadosDaContaInformados.CategoriaId), erro));
    }

    /// <summary>
    /// RN-23.7: os campos que o pedido muda numa conta com pagamento (categoria, valor, vencimento, fornecedor e número
    /// do documento), um erro por campo. O fornecedor é comparado pelo CNPJ/CPF (só dígitos).
    /// </summary>
    public static ValidationError[] CamposTravados(Conta conta, IDadosDaContaInformados pedido)
    {
        var documento = Documento.EstaVazio(pedido.FornecedorDocumento) ? null : Documento.SomenteDigitos(pedido.FornecedorDocumento);
        var dados = new DadosDaConta(
            pedido.Descricao, pedido.CategoriaId, pedido.Valor, pedido.Vencimento ?? default,
            documento is null ? null : new FornecedorDaConta(Guid.Empty, string.Empty, documento),
            pedido.NumeroDocumento, pedido.Observacao);

        return conta.CamposTravadosAlterados(dados)
            .Select(campo => new ValidationError(
                campo == nameof(DadosDaConta.Fornecedor) ? nameof(IDadosDaContaInformados.FornecedorDocumento) : campo,
                MensagensFinanceiro.CampoTravadoPeloPagamento))
            .ToArray();
    }

    public static DadosDaConta Dados(IDadosDaContaInformados c, FornecedorDaConta? fornecedor) =>
        new(c.Descricao, c.CategoriaId, c.Valor, c.Vencimento!.Value, fornecedor, c.NumeroDocumento, c.Observacao);
}

public sealed class LancarContaCommandValidator : AbstractValidator<LancarContaCommand>
{
    public LancarContaCommandValidator()
    {
        RegrasDaConta.Aplicar(this);
        RuleFor(c => c.Parcelas)
            .InclusiveBetween(1, Parcelamento.ParcelasMaximo)
            .WithMessage($"Informe de 1 a {Parcelamento.ParcelasMaximo} parcelas")
            // Fora da faixa, o erro já fica no Valor (e Valor * 100 não estoura).
            .Must((c, parcelas) => c.Valor <= 0 || c.Valor > Conta.ValorMaximo || c.Valor * 100 >= parcelas)
            .WithMessage("Cada parcela precisa ser de pelo menos R$ 0,01");
    }
}

public sealed class AlterarContaCommandValidator : AbstractValidator<AlterarContaCommand>
{
    public AlterarContaCommandValidator()
    {
        RuleFor(c => c.ContaId).NotEmpty().WithMessage("Informe a conta");
        RegrasDaConta.Aplicar(this);
    }
}

public sealed class LancarContaCommandHandler(
    IContasRepository contas, ICategoriasRepository categorias, IPessoasConsultas pessoas, TimeProvider relogio)
    : IRequestHandler<LancarContaCommand, Result<IReadOnlyList<ContaDto>>>
{
    public async Task<Result<IReadOnlyList<ContaDto>>> Handle(LancarContaCommand request, CancellationToken ct)
    {
        var (categoria, erroDaCategoria) = await RegrasDaConta.CategoriaAsync(categorias, request.CategoriaId, null, ct);
        var (fornecedor, erroDoFornecedor) = await RegrasDaConta.FornecedorAsync(pessoas, request.FornecedorDocumento, ct);
        ValidationError[] erros = [.. new[] { erroDaCategoria, erroDoFornecedor }.OfType<ValidationError>()];
        if (erros.Length > 0)
            return Result<IReadOnlyList<ContaDto>>.Invalid(erros);

        var parcelas = Conta.Lancar(RegrasDaConta.Dados(request, fornecedor), request.Parcelas, relogio.GetUtcNow());
        await contas.AdicionarAsync(parcelas, ct);

        return Result<IReadOnlyList<ContaDto>>.Created(parcelas.Select(c => ContaDto.De(c, categoria!.Nome)).ToList());
    }
}

public sealed class AlterarContaCommandHandler(
    IContasRepository contas, ICategoriasRepository categorias, IFormasDePagamentoRepository formas, IPessoasConsultas pessoas)
    : IRequestHandler<AlterarContaCommand, Result<ContaDto>>
{
    public async Task<Result<ContaDto>> Handle(AlterarContaCommand request, CancellationToken ct)
    {
        if (await contas.ObterAsync(request.ContaId, ct) is not { } conta)
            return Result<ContaDto>.NotFound(MensagensFinanceiro.ContaNaoEncontrada);

        // RN-23.7: com pagamento, só descrição e observação mudam; os outros campos precisam vir como estão.
        if (conta.TemPagamento && RegrasDaConta.CamposTravados(conta, request) is { Length: > 0 } travados)
            return Result<ContaDto>.Invalid(travados);

        var (categoria, erroDaCategoria) = await RegrasDaConta.CategoriaAsync(categorias, request.CategoriaId, conta.CategoriaId, ct);
        // Mesmo documento já gravado: mantém o fornecedor atual sem procurar de novo no Pessoas (o CNPJ/CPF pode ter
        // sido corrigido lá depois do lançamento).
        var (fornecedor, erroDoFornecedor) = conta.FornecedorId is { } fornecedorAtual
                                             && !Documento.EstaVazio(request.FornecedorDocumento)
                                             && Documento.SomenteDigitos(request.FornecedorDocumento) == conta.FornecedorDocumento
            ? (new FornecedorDaConta(fornecedorAtual, conta.FornecedorNome!, conta.FornecedorDocumento!), (ValidationError?)null)
            : await RegrasDaConta.FornecedorAsync(pessoas, request.FornecedorDocumento, ct);
        ValidationError[] erros = [.. new[] { erroDaCategoria, erroDoFornecedor }.OfType<ValidationError>()];
        if (erros.Length > 0)
            return Result<ContaDto>.Invalid(erros);

        conta.Alterar(RegrasDaConta.Dados(request, fornecedor));
        if (!await contas.SalvarAsync(ct))
            return Result<ContaDto>.Conflict(MensagensFinanceiro.ContaAlteradaAoMesmoTempo);
        return Result.Success(ContaDto.De(conta, categoria!.Nome, await NomesDasFormas.ListarAsync(formas, ct)));
    }
}

/// <summary>
/// RN-23.7: conta com pagamento (baixa não estornada) não pode ser excluída. Sem pagamento, é excluída com as baixas
/// estornadas que tiver.
/// </summary>
public sealed class ExcluirContaCommandHandler(IContasRepository contas) : IRequestHandler<ExcluirContaCommand, Result>
{
    public async Task<Result> Handle(ExcluirContaCommand request, CancellationToken ct)
    {
        if (await contas.ObterAsync(request.ContaId, ct) is not { } conta)
            return Result.NotFound(MensagensFinanceiro.ContaNaoEncontrada);

        if (conta.TemPagamento)
            return Result.Conflict(MensagensFinanceiro.ContaComPagamento);

        await contas.RemoverAsync(conta, ct);
        return Result.Success();
    }
}
