using Ardalis.Result;
using FluentValidation;
using Impa.Modules.Acesso.Application.Autenticacao;
using Impa.Modules.Acesso.Application.Autenticacao.Commands.Entrar;
using Impa.Modules.Acesso.Contracts;
using MediatR;

namespace Impa.Modules.Acesso.Application.Liberacoes.Commands.LiberarOperacao;

/// <summary>Token de uso único da liberação (vai só para a SPA, que o envia no cabeçalho <c>X-Liberacao</c>).</summary>
public sealed record LiberacaoDto(string Token, DateTimeOffset ExpiraEm)
{
    public override string ToString() => $"{nameof(LiberacaoDto)} {{ Token = ***, ExpiraEm = {ExpiraEm} }}";
}

/// <summary>
/// RN-1.9 / cenário 1.8: no terminal do operador (<paramref name="OperadorId"/>, sessão <paramref name="SessaoId"/>),
/// quem tem "liberar operações" e a <paramref name="Permissao"/> informa usuário e senha e libera uma operação.
/// </summary>
public sealed record LiberarOperacaoCommand(string Usuario, string Senha, string Permissao, Guid OperadorId, Guid SessaoId)
    : IRequest<Result<LiberacaoDto>>
{
    // A senha nunca vai para log (T3).
    public override string ToString() =>
        $"{nameof(LiberarOperacaoCommand)} {{ Usuario = {Usuario}, Senha = ***, Permissao = {Permissao}, OperadorId = {OperadorId}, SessaoId = {SessaoId} }}";
}

public sealed class LiberarOperacaoCommandValidator : AbstractValidator<LiberarOperacaoCommand>
{
    public LiberarOperacaoCommandValidator()
    {
        RuleFor(c => c.Usuario).NotEmpty().WithMessage("Informe o usuário");
        RuleFor(c => c.Senha).NotEmpty().WithMessage("Informe a senha");
        RuleFor(c => c.Permissao).Must(Permissoes.EhLiberavel).WithMessage(MensagensLiberacao.PermissaoNaoLiberavel);
        RuleFor(c => c.OperadorId).NotEmpty().WithMessage("Sessão sem usuário");
        RuleFor(c => c.SessaoId).NotEmpty().WithMessage("Sessão sem usuário");
    }
}

/// <summary>
/// Confere a credencial como o login (mesma resposta para usuário inexistente, desativado e senha errada; senha errada
/// conta para o bloqueio — RN-1.2, story 1-2) e só então olha se o usuário pode liberar. Recusas são 403, não 401:
/// a sessão do operador continua válida.
/// </summary>
public sealed class LiberarOperacaoCommandHandler(
    ISessaoService sessao,
    IPermissoesDoUsuario permissoes,
    ILiberacoes liberacoes,
    TimeProvider relogio) : IRequestHandler<LiberarOperacaoCommand, Result<LiberacaoDto>>
{
    public async Task<Result<LiberacaoDto>> Handle(LiberarOperacaoCommand request, CancellationToken ct)
    {
        var login = request.Usuario.Trim();

        var liberadorId = await sessao.BuscarUsuarioAsync(login, ct);
        if (liberadorId is null)
        {
            await sessao.ConferirSenhaFicticiaAsync(request.Senha, ct);
            return CredencialInvalida();
        }

        var senhaConfere = await sessao.SenhaConfereAsync(liberadorId.Value, request.Senha, ct);
        var situacao = await sessao.ObterSituacaoAsync(liberadorId.Value, ct);

        if (!situacao.Ativo)
            return CredencialInvalida();

        if (situacao.Bloqueada)
            return Bloqueado();

        if (!senhaConfere)
            return await sessao.RegistrarFalhaAsync(liberadorId.Value, ct) ? Bloqueado() : CredencialInvalida();

        if (!await sessao.ZerarFalhasAsync(liberadorId.Value, ct))
            return Bloqueado();

        // RN-1.9: "liberar operações" e a permissão exigida. Com a senha provisória, o usuário só pode trocá-la (RN-1.8).
        var doLiberador = await permissoes.ObterAsync(liberadorId.Value, ct);
        if (doLiberador.TrocaDeSenhaObrigatoria
            || !doLiberador.Tem(Permissoes.LiberarOperacoes)
            || !doLiberador.Tem(request.Permissao))
            return Result<LiberacaoDto>.Forbidden(MensagensLiberacao.SemPermissaoParaLiberar);

        var token = TokenDeLiberacao.Gerar();
        var expiraEm = relogio.GetUtcNow().Add(TokenDeLiberacao.Validade);
        await liberacoes.RegistrarAsync(new NovaLiberacao(
            TokenDeLiberacao.Hash(token), request.SessaoId, request.OperadorId, liberadorId.Value, request.Permissao, expiraEm), ct);

        return Result.Created(new LiberacaoDto(token, expiraEm));
    }

    private static Result<LiberacaoDto> CredencialInvalida() =>
        Result<LiberacaoDto>.Forbidden(EntrarCommand.MensagemCredencialInvalida);

    private static Result<LiberacaoDto> Bloqueado() => Result<LiberacaoDto>.Forbidden(EntrarCommand.MensagemBloqueado);
}
