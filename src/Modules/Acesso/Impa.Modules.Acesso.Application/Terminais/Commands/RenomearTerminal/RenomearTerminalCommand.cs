using Ardalis.Result;
using FluentValidation;
using MediatR;

namespace Impa.Modules.Acesso.Application.Terminais.Commands.RenomearTerminal;

/// <summary>Troca o nome de um terminal ativo (nome único entre os ativos).</summary>
public sealed record RenomearTerminalCommand(Guid Id, string Nome) : IRequest<Result<TerminalDto>>;

public sealed class RenomearTerminalCommandValidator : AbstractValidator<RenomearTerminalCommand>
{
    public RenomearTerminalCommandValidator()
    {
        RuleFor(c => c.Id).NotEmpty().WithMessage("Informe o terminal");
        RegrasDoTerminal.Nome(RuleFor(c => c.Nome));
    }
}

public sealed class RenomearTerminalCommandHandler(ITerminais terminais) : IRequestHandler<RenomearTerminalCommand, Result<TerminalDto>>
{
    public async Task<Result<TerminalDto>> Handle(RenomearTerminalCommand request, CancellationToken ct)
    {
        var atual = await terminais.ObterAsync(request.Id, ct);
        if (atual is null)
            return Result<TerminalDto>.NotFound(MensagensTerminais.NaoEncontrado);
        if (atual.RevogadoEm is not null)
            return Result<TerminalDto>.Conflict(MensagensTerminais.Revogado);

        var nome = RegrasDoTerminal.NormalizarNome(request.Nome);
        if (await terminais.NomeEmUsoAsync(nome, request.Id, ct))
            return Result<TerminalDto>.Conflict(MensagensTerminais.NomeEmUso);

        try
        {
            return Result.Success(await terminais.RenomearAsync(request.Id, nome, ct));
        }
        catch (NomeDeTerminalEmUsoException)
        {
            return Result<TerminalDto>.Conflict(MensagensTerminais.NomeEmUso);
        }
    }
}
