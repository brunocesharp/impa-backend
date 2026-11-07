using Ardalis.Result;
using FluentValidation;
using Impa.Modules.Pessoas.Application.Comum;
using Impa.Modules.Pessoas.Domain;
using MediatR;

namespace Impa.Modules.Pessoas.Application.Clientes.Commands;

/// <summary>Campos do formulário de cliente (RN-14.1).</summary>
public interface IDadosDoClienteInformados
{
    string Nome { get; }
    string? Documento { get; }
    string? Telefone { get; }
    string? Email { get; }
    string? Endereco { get; }
    string? Observacoes { get; }
}

/// <summary>Cenário 14.1: cadastra o cliente (no balcão, pelo operador de caixa).</summary>
public sealed record CadastrarClienteCommand(
    string Nome, string? Documento, string? Telefone, string? Email, string? Endereco, string? Observacoes)
    : IRequest<Result<ClienteDto>>, IDadosDoClienteInformados
{
    // RN-14.6: sem dados pessoais em log.
    public override string ToString() => $"{nameof(CadastrarClienteCommand)} {{ *** }}";
}

public sealed record AlterarClienteCommand(
    Guid ClienteId, string Nome, string? Documento, string? Telefone, string? Email, string? Endereco, string? Observacoes)
    : IRequest<Result<ClienteDto>>, IDadosDoClienteInformados
{
    public override string ToString() => $"{nameof(AlterarClienteCommand)} {{ ClienteId = {ClienteId}, *** }}";
}

internal static class RegrasDoCliente
{
    public static void Aplicar<T>(AbstractValidator<T> v) where T : IDadosDoClienteInformados
    {
        RegrasDePessoas.Obrigatorio(v.RuleFor(c => c.Nome), "Informe o nome", Cliente.NomeMaximo);
        RegrasDePessoas.DocumentoOpcional(v.RuleFor(c => c.Documento));
        RegrasDePessoas.Telefone(v.RuleFor(c => c.Telefone));
        RegrasDePessoas.Email(v.RuleFor(c => c.Email), Cliente.EmailMaximo);
        RegrasDePessoas.Opcional(v.RuleFor(c => c.Endereco), Cliente.EnderecoMaximo);
        RegrasDePessoas.Opcional(v.RuleFor(c => c.Observacoes), Cliente.ObservacoesMaximo);
    }

    public static DadosDoCliente Dados(IDadosDoClienteInformados c) =>
        new(c.Nome, RegrasDePessoas.DocumentoOuNulo(c.Documento), c.Telefone, c.Email, c.Endereco, c.Observacoes);

    /// <summary>Cenário 14.2: "CPF já cadastrado para Maria Souza". Nulo quando o documento está livre.</summary>
    public static async Task<string?> DocumentoRepetidoAsync(
        IClientesRepository clientes, DadosDoCliente dados, Guid? exceto, CancellationToken ct)
    {
        if (dados.Documento is null)
            return null;

        var outro = await clientes.ObterPorDocumentoAsync(dados.Documento.Numero, exceto, ct);
        return outro is null ? null : MensagensPessoas.DocumentoDeClienteRepetido(dados.Documento, outro.Nome);
    }
}

public sealed class CadastrarClienteCommandValidator : AbstractValidator<CadastrarClienteCommand>
{
    public CadastrarClienteCommandValidator() => RegrasDoCliente.Aplicar(this);
}

public sealed class AlterarClienteCommandValidator : AbstractValidator<AlterarClienteCommand>
{
    public AlterarClienteCommandValidator()
    {
        RuleFor(c => c.ClienteId).NotEmpty().WithMessage("Informe o cliente");
        RegrasDoCliente.Aplicar(this);
    }
}

public sealed class CadastrarClienteCommandHandler(IClientesRepository clientes, TimeProvider relogio)
    : IRequestHandler<CadastrarClienteCommand, Result<ClienteDto>>
{
    public async Task<Result<ClienteDto>> Handle(CadastrarClienteCommand request, CancellationToken ct)
    {
        var dados = RegrasDoCliente.Dados(request);
        if (await RegrasDoCliente.DocumentoRepetidoAsync(clientes, dados, null, ct) is { } repetido)
            return Result<ClienteDto>.Conflict(repetido);

        var cliente = Cliente.Criar(dados, relogio.GetUtcNow());

        // Documento livre na conferência acima, mas tomado por um cadastro simultâneo: o mesmo 409.
        if (!await clientes.AdicionarAsync(cliente, ct))
            return Result<ClienteDto>.Conflict(
                await RegrasDoCliente.DocumentoRepetidoAsync(clientes, dados, cliente.Id, ct) ?? MensagensPessoas.DocumentoJaCadastrado);

        return Result.Created(ClienteDto.De(cliente));
    }
}

public sealed class AlterarClienteCommandHandler(IClientesRepository clientes)
    : IRequestHandler<AlterarClienteCommand, Result<ClienteDto>>
{
    public async Task<Result<ClienteDto>> Handle(AlterarClienteCommand request, CancellationToken ct)
    {
        var cliente = await clientes.ObterAsync(request.ClienteId, ct);
        if (cliente is null)
            return Result<ClienteDto>.NotFound(MensagensPessoas.ClienteNaoEncontrado);

        var dados = RegrasDoCliente.Dados(request);
        if (await RegrasDoCliente.DocumentoRepetidoAsync(clientes, dados, cliente.Id, ct) is { } repetido)
            return Result<ClienteDto>.Conflict(repetido);

        cliente.Alterar(dados);
        if (!await clientes.SalvarAsync(ct))
            return Result<ClienteDto>.Conflict(
                await RegrasDoCliente.DocumentoRepetidoAsync(clientes, dados, cliente.Id, ct) ?? MensagensPessoas.DocumentoJaCadastrado);

        return Result.Success(ClienteDto.De(cliente));
    }
}
