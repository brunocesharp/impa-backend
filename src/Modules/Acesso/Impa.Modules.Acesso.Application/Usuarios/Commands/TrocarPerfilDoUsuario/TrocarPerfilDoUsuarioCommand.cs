using Ardalis.Result;
using FluentValidation;
using Impa.Modules.Acesso.Application.Perfis;
using Impa.Modules.Acesso.Application.Usuarios.Commands.DesativarUsuario;
using MediatR;

namespace Impa.Modules.Acesso.Application.Usuarios.Commands.TrocarPerfilDoUsuario;

/// <summary>
/// Troca o perfil de um usuário (RN-1.4: um perfil por usuário). A regra do último administrador (cenário 1.6) é
/// desta story; a rota e a tela de usuários que usam este caso de uso vêm na story 1.4.
/// </summary>
public sealed record TrocarPerfilDoUsuarioCommand(Guid UsuarioId, Guid PerfilId) : IRequest<Result>;

public sealed class TrocarPerfilDoUsuarioCommandValidator : AbstractValidator<TrocarPerfilDoUsuarioCommand>
{
    public TrocarPerfilDoUsuarioCommandValidator()
    {
        RuleFor(c => c.UsuarioId).NotEmpty().WithMessage("Informe o usuário");
        RuleFor(c => c.PerfilId).NotEmpty().WithMessage("Informe o perfil");
    }
}

public sealed class TrocarPerfilDoUsuarioCommandHandler(IGestaoUsuarios usuarios, IGestaoPerfis perfis)
    : IRequestHandler<TrocarPerfilDoUsuarioCommand, Result>
{
    public async Task<Result> Handle(TrocarPerfilDoUsuarioCommand request, CancellationToken ct)
    {
        if (!await usuarios.ExisteAsync(request.UsuarioId, ct))
            return Result.NotFound(DesativarUsuarioCommand.MensagemNaoEncontrado);

        var perfil = await perfis.ObterAsync(request.PerfilId, ct);
        if (perfil is null)
            return Result.NotFound(MensagensPerfis.NaoEncontrado);

        return await usuarios.ComAdministradoresTravadosAsync(async () =>
        {
            if (!perfil.Protegido
                && await usuarios.EhAdministradorAtivoAsync(request.UsuarioId, ct)
                && await usuarios.ContarAdministradoresAtivosAsync(ct) <= 1)
                return Result.Conflict(MensagensUsuarios.UltimoAdministrador);

            await usuarios.TrocarPerfilAsync(request.UsuarioId, request.PerfilId, ct);
            return Result.Success();
        }, ct);
    }
}
