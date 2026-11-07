using Ardalis.Result;
using FluentValidation;
using Impa.Modules.Acesso.Application.Usuarios;
using MediatR;

namespace Impa.Modules.Acesso.Application.Autenticacao.Commands.TrocarSenha;

/// <summary>
/// O usuário da sessão troca a própria senha. Com a senha provisória (RN-1.8), é a única operação liberada até a
/// troca; depois dela, a marca de troca obrigatória sai e o acesso volta ao normal, sem novo login (cenário 1.7).
/// </summary>
public sealed record TrocarSenhaCommand(string SenhaAtual, string NovaSenha) : IRequest<Result>
{
    public const string MensagemSenhaAtualIncorreta = "Senha atual incorreta";
    public const string MensagemSenhaIgual = "A nova senha precisa ser diferente da atual";

    public override string ToString() => $"{nameof(TrocarSenhaCommand)} {{ SenhaAtual = ***, NovaSenha = *** }}";
}

public sealed class TrocarSenhaCommandValidator : AbstractValidator<TrocarSenhaCommand>
{
    public TrocarSenhaCommandValidator()
    {
        RuleFor(c => c.SenhaAtual).NotEmpty().WithMessage("Informe a senha atual");
        RuleFor(c => c.NovaSenha)
            .Must(s => s is not null && s.Length >= RegrasDoUsuario.SenhaMinima).WithMessage(RegrasDoUsuario.MensagemSenhaCurta)
            .Must((c, s) => s != c.SenhaAtual).WithMessage(TrocarSenhaCommand.MensagemSenhaIgual);
    }
}

/// <summary>Primitivas da troca de senha do usuário da sessão (implementadas na Infrastructure com o Identity).</summary>
public interface ITrocaDeSenha
{
    /// <summary>Id do usuário da sessão atual; <c>null</c> sem sessão.</summary>
    Guid? UsuarioDaSessao();

    /// <summary>
    /// Grava a nova senha, tira a marca de troca obrigatória e renova o cookie da sessão (o security stamp muda e
    /// a sessão atual continua valendo). Devolve os erros do validador de senha do Identity, ou vazio.
    /// </summary>
    Task<IReadOnlyList<string>> TrocarAsync(Guid usuarioId, string novaSenha, CancellationToken ct);
}

public sealed class TrocarSenhaCommandHandler(ITrocaDeSenha troca, ISessaoService sessao) : IRequestHandler<TrocarSenhaCommand, Result>
{
    public async Task<Result> Handle(TrocarSenhaCommand request, CancellationToken ct)
    {
        if (troca.UsuarioDaSessao() is not { } usuarioId)
            return Result.Unauthorized();

        if (!await sessao.SenhaConfereAsync(usuarioId, request.SenhaAtual, ct))
            return Result.Invalid(new ValidationError(nameof(request.SenhaAtual), TrocarSenhaCommand.MensagemSenhaAtualIncorreta));

        var erros = await troca.TrocarAsync(usuarioId, request.NovaSenha, ct);
        return erros.Count == 0
            ? Result.Success()
            : Result.Invalid(erros.Select(e => new ValidationError(nameof(request.NovaSenha), e)).ToList());
    }
}
