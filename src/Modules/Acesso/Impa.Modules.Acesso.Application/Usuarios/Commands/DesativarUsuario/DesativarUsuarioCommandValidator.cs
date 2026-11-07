using FluentValidation;

namespace Impa.Modules.Acesso.Application.Usuarios.Commands.DesativarUsuario;

public sealed class DesativarUsuarioCommandValidator : AbstractValidator<DesativarUsuarioCommand>
{
    public DesativarUsuarioCommandValidator() =>
        RuleFor(c => c.UsuarioId).NotEmpty().WithMessage("Informe o usuário");
}
