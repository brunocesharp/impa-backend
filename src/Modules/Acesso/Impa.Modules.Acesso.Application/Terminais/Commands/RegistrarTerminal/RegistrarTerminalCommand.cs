using Ardalis.Result;
using FluentValidation;
using MediatR;

namespace Impa.Modules.Acesso.Application.Terminais.Commands.RegistrarTerminal;

/// <summary>Terminal registrado e o token novo (vai só para o cookie do dispositivo; o banco guarda o hash).</summary>
public sealed record TerminalRegistradoDto(TerminalDto Terminal, string Token)
{
    public override string ToString() => $"{nameof(TerminalRegistradoDto)} {{ Terminal = {Terminal}, Token = *** }}";
}

/// <summary>
/// RN-11.1: o administrador registra o dispositivo atual como terminal, com um nome. <paramref name="TokenAtual"/> é o
/// cookie de terminal que o dispositivo já tem (se tiver): um dispositivo com terminal válido não é registrado de novo.
/// </summary>
public sealed record RegistrarTerminalCommand(string Nome, Guid CriadoPor, string? TokenAtual) : IRequest<Result<TerminalRegistradoDto>>
{
    public override string ToString() => $"{nameof(RegistrarTerminalCommand)} {{ Nome = {Nome}, CriadoPor = {CriadoPor}, TokenAtual = *** }}";
}

public sealed class RegistrarTerminalCommandValidator : AbstractValidator<RegistrarTerminalCommand>
{
    public RegistrarTerminalCommandValidator()
    {
        RegrasDoTerminal.Nome(RuleFor(c => c.Nome));
        RuleFor(c => c.CriadoPor).NotEmpty().WithMessage("Sessão sem usuário");
    }
}

public sealed class RegistrarTerminalCommandHandler(ITerminais terminais)
    : IRequestHandler<RegistrarTerminalCommand, Result<TerminalRegistradoDto>>
{
    public async Task<Result<TerminalRegistradoDto>> Handle(RegistrarTerminalCommand request, CancellationToken ct)
    {
        if (TokenDoTerminal.HashSeValido(request.TokenAtual) is { } hashAtual
            && await terminais.ObterValidoAsync(hashAtual, ct) is { } jaRegistrado)
            return Result<TerminalRegistradoDto>.Conflict(MensagensTerminais.DispositivoJaRegistrado(jaRegistrado.Nome));

        var nome = RegrasDoTerminal.NormalizarNome(request.Nome);
        if (await terminais.NomeEmUsoAsync(nome, null, ct))
            return Result<TerminalRegistradoDto>.Conflict(MensagensTerminais.NomeEmUso);

        var token = TokenDoTerminal.Gerar();
        try
        {
            var terminal = await terminais.RegistrarAsync(nome, request.CriadoPor, TokenDoTerminal.Hash(token), ct);
            return Result.Created(new TerminalRegistradoDto(terminal, token));
        }
        catch (NomeDeTerminalEmUsoException)
        {
            return Result<TerminalRegistradoDto>.Conflict(MensagensTerminais.NomeEmUso);
        }
    }
}
