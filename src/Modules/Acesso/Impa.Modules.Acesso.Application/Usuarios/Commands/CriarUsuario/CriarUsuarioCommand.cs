using Ardalis.Result;
using FluentValidation;
using Impa.Modules.Acesso.Application.Perfis;
using MediatR;

namespace Impa.Modules.Acesso.Application.Usuarios.Commands.CriarUsuario;

/// <summary>
/// Cria um usuário com um perfil (RN-1.4) e a senha inicial provisória: informada pelo administrador ou, sem
/// <paramref name="Senha"/>, gerada pelo sistema. O usuário troca a senha no primeiro acesso (RN-1.8).
/// </summary>
public sealed record CriarUsuarioCommand(string Usuario, string Nome, string? Email, Guid PerfilId, string? Senha)
    : IRequest<Result<SenhaProvisoriaDto>>
{
    public override string ToString() =>
        $"{nameof(CriarUsuarioCommand)} {{ Usuario = {Usuario}, Nome = {Nome}, Email = {Email}, PerfilId = {PerfilId}, Senha = *** }}";
}

public sealed class CriarUsuarioCommandValidator : AbstractValidator<CriarUsuarioCommand>
{
    public CriarUsuarioCommandValidator()
    {
        RegrasDoUsuario.Login(RuleFor(c => c.Usuario));
        RegrasDoUsuario.Nome(RuleFor(c => c.Nome));
        RegrasDoUsuario.Email(RuleFor(c => c.Email));
        RuleFor(c => c.PerfilId).NotEmpty().WithMessage("Informe o perfil");
        RegrasDoUsuario.SenhaOpcional(RuleFor(c => c.Senha));
    }
}

public sealed class CriarUsuarioCommandHandler(ICadastroUsuarios cadastro, IGestaoPerfis perfis)
    : IRequestHandler<CriarUsuarioCommand, Result<SenhaProvisoriaDto>>
{
    public async Task<Result<SenhaProvisoriaDto>> Handle(CriarUsuarioCommand request, CancellationToken ct)
    {
        var login = RegrasDoUsuario.NormalizarTexto(request.Usuario);
        if (await cadastro.LoginEmUsoAsync(login, ct))
            return Result<SenhaProvisoriaDto>.Conflict(MensagensUsuarios.LoginEmUso);

        if (await perfis.ObterAsync(request.PerfilId, ct) is null)
            return Result<SenhaProvisoriaDto>.NotFound(MensagensPerfis.NaoEncontrado);

        var senha = request.Senha ?? RegrasDoUsuario.GerarSenhaProvisoria();
        var dados = new NovoUsuario(login, RegrasDoUsuario.NormalizarTexto(request.Nome),
            RegrasDoUsuario.NormalizarEmail(request.Email), request.PerfilId);

        // Login livre na conferência acima, mas tomado por outro cadastro simultâneo: o mesmo 409.
        if (await cadastro.CriarAsync(dados, senha, ct) is not { } id)
            return Result<SenhaProvisoriaDto>.Conflict(MensagensUsuarios.LoginEmUso);

        return Result.Created(new SenhaProvisoriaDto(id, senha));
    }
}
