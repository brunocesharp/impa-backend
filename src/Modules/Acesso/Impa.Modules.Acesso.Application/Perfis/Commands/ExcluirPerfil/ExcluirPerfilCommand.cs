using Ardalis.Result;
using FluentValidation;
using MediatR;

namespace Impa.Modules.Acesso.Application.Perfis.Commands.ExcluirPerfil;

/// <summary>Exclui um perfil sem usuários. O Administrador não pode ser excluído (RN-1.6).</summary>
public sealed record ExcluirPerfilCommand(Guid Id) : IRequest<Result>;

public sealed class ExcluirPerfilCommandValidator : AbstractValidator<ExcluirPerfilCommand>
{
    public ExcluirPerfilCommandValidator() => RuleFor(c => c.Id).NotEmpty().WithMessage("Informe o perfil");
}

public sealed class ExcluirPerfilCommandHandler(IGestaoPerfis perfis) : IRequestHandler<ExcluirPerfilCommand, Result>
{
    public async Task<Result> Handle(ExcluirPerfilCommand request, CancellationToken ct)
    {
        var perfil = await perfis.ObterAsync(request.Id, ct);
        if (perfil is null)
            return Result.NotFound(MensagensPerfis.NaoEncontrado);
        if (perfil.Protegido)
            return Result.Conflict(MensagensPerfis.AdministradorNaoExcluivel);
        if (perfil.Usuarios > 0)
            return Result.Conflict(MensagensPerfis.PerfilComUsuarios);

        await perfis.ExcluirAsync(request.Id, ct);
        return Result.Success();
    }
}
