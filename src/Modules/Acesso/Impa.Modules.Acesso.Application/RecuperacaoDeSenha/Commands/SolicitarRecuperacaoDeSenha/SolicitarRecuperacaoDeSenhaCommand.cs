using Ardalis.Result;
using FluentValidation;
using MediatR;

namespace Impa.Modules.Acesso.Application.RecuperacaoDeSenha.Commands.SolicitarRecuperacaoDeSenha;

/// <summary>
/// "Esqueci minha senha" informando o login ou o e-mail (cenários 1.9 e 1.10). A resposta é sempre a mesma, exista
/// ou não o usuário (RN-1.11); o e-mail sai depois, pelo Outbox, e uma falha do Brevo nunca chega a esta resposta.
/// </summary>
public sealed record SolicitarRecuperacaoDeSenhaCommand(string UsuarioOuEmail) : IRequest<Result>
{
    public const int Maximo = 256;
}

public sealed class SolicitarRecuperacaoDeSenhaCommandValidator : AbstractValidator<SolicitarRecuperacaoDeSenhaCommand>
{
    public SolicitarRecuperacaoDeSenhaCommandValidator()
    {
        RuleFor(c => c.UsuarioOuEmail)
            .Must(v => !string.IsNullOrWhiteSpace(v)).WithMessage("Informe o usuário ou o e-mail")
            .MaximumLength(SolicitarRecuperacaoDeSenhaCommand.Maximo)
            .WithMessage($"Informe no máximo {SolicitarRecuperacaoDeSenhaCommand.Maximo} caracteres");
    }
}

public sealed class SolicitarRecuperacaoDeSenhaCommandHandler(IRecuperacaoDeSenha recuperacao)
    : IRequestHandler<SolicitarRecuperacaoDeSenhaCommand, Result>
{
    public async Task<Result> Handle(SolicitarRecuperacaoDeSenhaCommand request, CancellationToken ct)
    {
        await recuperacao.SolicitarAsync(request.UsuarioOuEmail.Trim(), ct);
        return Result.Success();
    }
}
