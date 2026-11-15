using Ardalis.Result;
using FluentValidation;
using Impa.Modules.Acesso.Application.Autenticacao;
using Impa.Modules.Acesso.Application.Usuarios;
using MediatR;

namespace Impa.Modules.Acesso.Application.RecuperacaoDeSenha.Commands.RedefinirSenhaPorLink;

/// <summary>Nova senha a partir do link do e-mail (cenário 1.9, RN-1.11). O link vale uma vez, por 30 minutos.</summary>
public sealed record RedefinirSenhaPorLinkCommand(string Token, string NovaSenha) : IRequest<Result>
{
    public const string MensagemLinkInvalido = "Link inválido ou expirado";

    public override string ToString() => $"{nameof(RedefinirSenhaPorLinkCommand)} {{ Token = ***, NovaSenha = *** }}";
}

public sealed class RedefinirSenhaPorLinkCommandValidator : AbstractValidator<RedefinirSenhaPorLinkCommand>
{
    public RedefinirSenhaPorLinkCommandValidator()
    {
        RuleFor(c => c.Token).NotEmpty().WithMessage(RedefinirSenhaPorLinkCommand.MensagemLinkInvalido);
        RuleFor(c => c.NovaSenha)
            .Must(s => s is not null && s.Length >= RegrasDoUsuario.SenhaMinima).WithMessage(RegrasDoUsuario.MensagemSenhaCurta);
    }
}

public sealed class RedefinirSenhaPorLinkCommandHandler(IRecuperacaoDeSenha recuperacao, ISessaoService sessao)
    : IRequestHandler<RedefinirSenhaPorLinkCommand, Result>
{
    public async Task<Result> Handle(RedefinirSenhaPorLinkCommand request, CancellationToken ct)
    {
        var resultado = await recuperacao.RedefinirAsync(request.Token, request.NovaSenha, ct);
        if (resultado.LinkInvalido)
            return Result.Invalid(new ValidationError(nameof(request.Token), RedefinirSenhaPorLinkCommand.MensagemLinkInvalido));

        if (resultado.ErrosDaSenha.Count > 0)
            return Result.Invalid(resultado.ErrosDaSenha.Select(e => new ValidationError(nameof(request.NovaSenha), e)).ToList());

        // A sessão deste navegador já foi revogada no banco junto com as outras do usuário; sai também do cookie.
        // Sessão de outro usuário no mesmo navegador (ex.: operador logado no caixa) continua aberta (R6, revisão 1).
        await sessao.SairSeForDoUsuarioAsync(resultado.UsuarioId!.Value, ct);
        return Result.Success();
    }
}
