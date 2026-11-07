using Ardalis.Result;
using FluentValidation;
using MediatR;

namespace Impa.Modules.Acesso.Application.Usuarios.Commands.RedefinirSenha;

/// <summary>
/// RN-1.8: o administrador redefine a senha de um usuário, que recebe uma senha provisória e é obrigado a trocá-la
/// no próximo acesso. Sem <paramref name="Senha"/>, o sistema gera a provisória.
/// </summary>
public sealed record RedefinirSenhaCommand(Guid UsuarioId, string? Senha) : IRequest<Result<SenhaProvisoriaDto>>
{
    public override string ToString() => $"{nameof(RedefinirSenhaCommand)} {{ UsuarioId = {UsuarioId}, Senha = *** }}";
}

public sealed class RedefinirSenhaCommandValidator : AbstractValidator<RedefinirSenhaCommand>
{
    public RedefinirSenhaCommandValidator()
    {
        RuleFor(c => c.UsuarioId).NotEmpty().WithMessage("Informe o usuário");
        RegrasDoUsuario.SenhaOpcional(RuleFor(c => c.Senha));
    }
}

public sealed class RedefinirSenhaCommandHandler(IGestaoUsuarios usuarios, ICadastroUsuarios cadastro)
    : IRequestHandler<RedefinirSenhaCommand, Result<SenhaProvisoriaDto>>
{
    public async Task<Result<SenhaProvisoriaDto>> Handle(RedefinirSenhaCommand request, CancellationToken ct)
    {
        if (!await usuarios.ExisteAsync(request.UsuarioId, ct))
            return Result<SenhaProvisoriaDto>.NotFound(MensagensUsuarios.NaoEncontrado);

        var senha = request.Senha ?? RegrasDoUsuario.GerarSenhaProvisoria();
        await cadastro.DefinirSenhaProvisoriaAsync(request.UsuarioId, senha, ct);
        return Result.Success(new SenhaProvisoriaDto(request.UsuarioId, senha));
    }
}
