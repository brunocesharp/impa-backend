using Ardalis.Result;
using FluentValidation;
using MediatR;

namespace Impa.Modules.Acesso.Application.Usuarios.Commands.ReativarUsuario;

/// <summary>Reativa um usuário desativado (o contrário da desativação da story 1-2, RN-1.7).</summary>
public sealed record ReativarUsuarioCommand(Guid UsuarioId) : IRequest<Result>;

public sealed class ReativarUsuarioCommandValidator : AbstractValidator<ReativarUsuarioCommand>
{
    public ReativarUsuarioCommandValidator() =>
        RuleFor(c => c.UsuarioId).NotEmpty().WithMessage("Informe o usuário");
}

public sealed class ReativarUsuarioCommandHandler(IGestaoUsuarios usuarios, ICadastroUsuarios cadastro)
    : IRequestHandler<ReativarUsuarioCommand, Result>
{
    public async Task<Result> Handle(ReativarUsuarioCommand request, CancellationToken ct)
    {
        if (!await usuarios.ExisteAsync(request.UsuarioId, ct))
            return Result.NotFound(MensagensUsuarios.NaoEncontrado);

        await cadastro.ReativarAsync(request.UsuarioId, ct);
        return Result.Success();
    }
}
