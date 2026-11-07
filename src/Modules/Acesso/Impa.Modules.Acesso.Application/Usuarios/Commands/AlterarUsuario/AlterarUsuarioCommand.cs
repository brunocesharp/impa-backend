using Ardalis.Result;
using FluentValidation;
using Impa.Modules.Acesso.Application.Perfis;
using MediatR;

namespace Impa.Modules.Acesso.Application.Usuarios.Commands.AlterarUsuario;

/// <summary>
/// Troca nome, e-mail e perfil de um usuário. O login não muda. Tirar o perfil Administrador do único administrador
/// ativo é recusado (cenário 1.6, RN-1.6).
/// </summary>
public sealed record AlterarUsuarioCommand(Guid UsuarioId, string Nome, string? Email, Guid PerfilId)
    : IRequest<Result<UsuarioDto>>;

public sealed class AlterarUsuarioCommandValidator : AbstractValidator<AlterarUsuarioCommand>
{
    public AlterarUsuarioCommandValidator()
    {
        RuleFor(c => c.UsuarioId).NotEmpty().WithMessage("Informe o usuário");
        RegrasDoUsuario.Nome(RuleFor(c => c.Nome));
        RegrasDoUsuario.Email(RuleFor(c => c.Email));
        RuleFor(c => c.PerfilId).NotEmpty().WithMessage("Informe o perfil");
    }
}

public sealed class AlterarUsuarioCommandHandler(IGestaoUsuarios usuarios, ICadastroUsuarios cadastro, IGestaoPerfis perfis)
    : IRequestHandler<AlterarUsuarioCommand, Result<UsuarioDto>>
{
    public async Task<Result<UsuarioDto>> Handle(AlterarUsuarioCommand request, CancellationToken ct)
    {
        if (!await usuarios.ExisteAsync(request.UsuarioId, ct))
            return Result<UsuarioDto>.NotFound(MensagensUsuarios.NaoEncontrado);

        var perfil = await perfis.ObterAsync(request.PerfilId, ct);
        if (perfil is null)
            return Result<UsuarioDto>.NotFound(MensagensPerfis.NaoEncontrado);

        var resultado = await usuarios.ComAdministradoresTravadosAsync(async () =>
        {
            if (!perfil.Protegido
                && await usuarios.EhAdministradorAtivoAsync(request.UsuarioId, ct)
                && await usuarios.ContarAdministradoresAtivosAsync(ct) <= 1)
                return Result.Conflict(MensagensUsuarios.UltimoAdministrador);

            await cadastro.AlterarDadosAsync(request.UsuarioId, RegrasDoUsuario.NormalizarTexto(request.Nome),
                RegrasDoUsuario.NormalizarEmail(request.Email), ct);
            await usuarios.TrocarPerfilAsync(request.UsuarioId, request.PerfilId, ct);
            return Result.Success();
        }, ct);

        if (!resultado.IsSuccess)
            return Result<UsuarioDto>.Conflict([.. resultado.Errors]);

        return Result.Success((await cadastro.ObterAsync(request.UsuarioId, ct))!);
    }
}
