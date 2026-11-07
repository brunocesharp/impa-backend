using Ardalis.Result;
using FluentValidation;
using Impa.Modules.Pessoas.Application.Comum;
using Impa.Modules.Pessoas.Contracts;
using Impa.Modules.Pessoas.Domain;
using MediatR;

namespace Impa.Modules.Pessoas.Application.Fornecedores.Commands;

/// <summary>Campos do formulário de fornecedor (RN-14.2).</summary>
public interface IDadosDoFornecedorInformados
{
    string RazaoSocial { get; }
    string? Documento { get; }
    string? NomeFantasia { get; }
    string? Telefone { get; }
    string? Email { get; }
    string? Contato { get; }
}

public sealed record CadastrarFornecedorCommand(
    string RazaoSocial, string? Documento, string? NomeFantasia, string? Telefone, string? Email, string? Contato)
    : IRequest<Result<FornecedorDto>>, IDadosDoFornecedorInformados
{
    // RN-14.6: fornecedor pessoa física tem CPF; nada de dados de contato em log.
    public override string ToString() => $"{nameof(CadastrarFornecedorCommand)} {{ *** }}";
}

public sealed record AlterarFornecedorCommand(
    Guid FornecedorId, string RazaoSocial, string? Documento, string? NomeFantasia, string? Telefone, string? Email, string? Contato)
    : IRequest<Result<FornecedorDto>>, IDadosDoFornecedorInformados
{
    public override string ToString() => $"{nameof(AlterarFornecedorCommand)} {{ FornecedorId = {FornecedorId}, *** }}";
}

public sealed record InativarFornecedorCommand(Guid FornecedorId) : IRequest<Result>;

public sealed record ReativarFornecedorCommand(Guid FornecedorId) : IRequest<Result>;

/// <summary>RN-14.4: só exclui fornecedor sem notas nem contas vinculadas.</summary>
public sealed record ExcluirFornecedorCommand(Guid FornecedorId) : IRequest<Result>;

internal static class RegrasDoFornecedor
{
    public static void Aplicar<T>(AbstractValidator<T> v) where T : IDadosDoFornecedorInformados
    {
        RegrasDePessoas.Obrigatorio(v.RuleFor(c => c.RazaoSocial), "Informe a razão social", Fornecedor.RazaoSocialMaximo);
        RegrasDePessoas.DocumentoObrigatorio(v.RuleFor(c => c.Documento));
        RegrasDePessoas.Opcional(v.RuleFor(c => c.NomeFantasia), Fornecedor.NomeFantasiaMaximo);
        RegrasDePessoas.Telefone(v.RuleFor(c => c.Telefone));
        RegrasDePessoas.Email(v.RuleFor(c => c.Email), Fornecedor.EmailMaximo);
        RegrasDePessoas.Opcional(v.RuleFor(c => c.Contato), Fornecedor.ContatoMaximo);
    }

    public static DadosDoFornecedor Dados(IDadosDoFornecedorInformados c) =>
        new(c.RazaoSocial, RegrasDePessoas.DocumentoOuNulo(c.Documento)!, c.NomeFantasia, c.Telefone, c.Email, c.Contato);

    /// <summary>"CNPJ já cadastrado para o fornecedor {razão social}". Nulo quando o documento está livre.</summary>
    public static async Task<string?> DocumentoRepetidoAsync(
        IFornecedoresRepository fornecedores, DadosDoFornecedor dados, Guid? exceto, CancellationToken ct)
    {
        var outro = await fornecedores.ObterPorDocumentoAsync(dados.Documento.Numero, exceto, ct);
        return outro is null ? null : MensagensPessoas.DocumentoDeFornecedorRepetido(dados.Documento, outro.RazaoSocial);
    }
}

public sealed class CadastrarFornecedorCommandValidator : AbstractValidator<CadastrarFornecedorCommand>
{
    public CadastrarFornecedorCommandValidator() => RegrasDoFornecedor.Aplicar(this);
}

public sealed class AlterarFornecedorCommandValidator : AbstractValidator<AlterarFornecedorCommand>
{
    public AlterarFornecedorCommandValidator()
    {
        RuleFor(c => c.FornecedorId).NotEmpty().WithMessage("Informe o fornecedor");
        RegrasDoFornecedor.Aplicar(this);
    }
}

public sealed class CadastrarFornecedorCommandHandler(IFornecedoresRepository fornecedores, TimeProvider relogio)
    : IRequestHandler<CadastrarFornecedorCommand, Result<FornecedorDto>>
{
    public async Task<Result<FornecedorDto>> Handle(CadastrarFornecedorCommand request, CancellationToken ct)
    {
        var dados = RegrasDoFornecedor.Dados(request);
        if (await RegrasDoFornecedor.DocumentoRepetidoAsync(fornecedores, dados, null, ct) is { } repetido)
            return Result<FornecedorDto>.Conflict(repetido);

        var fornecedor = Fornecedor.Criar(dados, relogio.GetUtcNow());
        if (!await fornecedores.AdicionarAsync(fornecedor, ct))
            return Result<FornecedorDto>.Conflict(
                await RegrasDoFornecedor.DocumentoRepetidoAsync(fornecedores, dados, fornecedor.Id, ct)
                ?? MensagensPessoas.DocumentoJaCadastrado);

        return Result.Created(FornecedorDto.De(fornecedor));
    }
}

public sealed class AlterarFornecedorCommandHandler(IFornecedoresRepository fornecedores)
    : IRequestHandler<AlterarFornecedorCommand, Result<FornecedorDto>>
{
    public async Task<Result<FornecedorDto>> Handle(AlterarFornecedorCommand request, CancellationToken ct)
    {
        var fornecedor = await fornecedores.ObterAsync(request.FornecedorId, ct);
        if (fornecedor is null)
            return Result<FornecedorDto>.NotFound(MensagensPessoas.FornecedorNaoEncontrado);

        var dados = RegrasDoFornecedor.Dados(request);
        if (await RegrasDoFornecedor.DocumentoRepetidoAsync(fornecedores, dados, fornecedor.Id, ct) is { } repetido)
            return Result<FornecedorDto>.Conflict(repetido);

        fornecedor.Alterar(dados);
        if (!await fornecedores.SalvarAsync(ct))
            return Result<FornecedorDto>.Conflict(
                await RegrasDoFornecedor.DocumentoRepetidoAsync(fornecedores, dados, fornecedor.Id, ct)
                ?? MensagensPessoas.DocumentoJaCadastrado);

        return Result.Success(FornecedorDto.De(fornecedor));
    }
}

public sealed class InativarFornecedorCommandHandler(IFornecedoresRepository fornecedores)
    : IRequestHandler<InativarFornecedorCommand, Result>
{
    public async Task<Result> Handle(InativarFornecedorCommand request, CancellationToken ct)
    {
        if (await fornecedores.ObterAsync(request.FornecedorId, ct) is not { } fornecedor)
            return Result.NotFound(MensagensPessoas.FornecedorNaoEncontrado);

        fornecedor.Inativar();
        await fornecedores.SalvarAsync(ct);
        return Result.Success();
    }
}

public sealed class ReativarFornecedorCommandHandler(IFornecedoresRepository fornecedores)
    : IRequestHandler<ReativarFornecedorCommand, Result>
{
    public async Task<Result> Handle(ReativarFornecedorCommand request, CancellationToken ct)
    {
        if (await fornecedores.ObterAsync(request.FornecedorId, ct) is not { } fornecedor)
            return Result.NotFound(MensagensPessoas.FornecedorNaoEncontrado);

        fornecedor.Reativar();
        await fornecedores.SalvarAsync(ct);
        return Result.Success();
    }
}

public sealed class ExcluirFornecedorCommandHandler(
    IFornecedoresRepository fornecedores, IEnumerable<IHistoricoDaPessoa> historicos)
    : IRequestHandler<ExcluirFornecedorCommand, Result>
{
    public async Task<Result> Handle(ExcluirFornecedorCommand request, CancellationToken ct)
    {
        if (await fornecedores.ObterAsync(request.FornecedorId, ct) is not { } fornecedor)
            return Result.NotFound(MensagensPessoas.FornecedorNaoEncontrado);

        if (await HistoricoDasPessoas.TemAsync(historicos, TipoDePessoa.Fornecedor, fornecedor.Id, ct))
            return Result.Conflict(MensagensPessoas.FornecedorComHistorico);

        await fornecedores.RemoverAsync(fornecedor, ct);
        return Result.Success();
    }
}
