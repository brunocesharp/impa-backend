using FluentValidation;

namespace Impa.Modules.Acesso.Application.Autenticacao.Commands.Entrar;

public sealed class EntrarCommandValidator : AbstractValidator<EntrarCommand>
{
    public EntrarCommandValidator()
    {
        // NotEmpty já recusa texto só com espaços.
        RuleFor(x => x.Usuario).NotEmpty().WithMessage("Informe o usuário");
        RuleFor(x => x.Senha).NotEmpty().WithMessage("Informe a senha");
    }
}
