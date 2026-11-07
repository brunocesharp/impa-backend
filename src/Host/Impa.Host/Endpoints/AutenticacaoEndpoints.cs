using Impa.Host.Http;
using ResultStatus = Ardalis.Result.ResultStatus;
using Impa.Host.Seguranca;
using Impa.Modules.Acesso.Application.Autenticacao;
using Impa.Modules.Acesso.Application.Autenticacao.Commands.Entrar;
using Impa.Modules.Acesso.Application.Autenticacao.Commands.Sair;
using Impa.Modules.Acesso.Application.Autenticacao.Commands.TrocarSenha;
using Impa.Modules.Acesso.Application.Autenticacao.Queries.ObterSessaoAtual;
using MediatR;

namespace Impa.Host.Endpoints;

public sealed record EntrarRequest(string? Usuario, string? Senha);

public sealed record TrocarSenhaRequest(string? SenhaAtual, string? NovaSenha)
{
    public override string ToString() => $"{nameof(TrocarSenhaRequest)} {{ SenhaAtual = ***, NovaSenha = *** }}";
}

/// <summary><c>/api/auth</c>: login, logout e sessão atual (ADR-006).</summary>
public static class AutenticacaoEndpoints
{
    /// <summary><c>codigo</c> do 401 de login quando a conta está bloqueada por tentativas (RN-1.2).</summary>
    public const string CodigoBloqueado = "bloqueado";

    public static IEndpointRouteBuilder MapAutenticacaoEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/auth").WithTags("Autenticação");

        grupo.MapPost("/login", Entrar)
            .AllowAnonymous()
            .RequireRateLimiting(LimiteDeLogin.Politica)
            .WithName("Entrar")
            .WithSummary("Entra com usuário e senha e abre a sessão por cookie")
            .Produces(StatusCodes.Status204NoContent)
            // 401 { mensagem } de credencial inválida, ou { mensagem, codigo: "bloqueado" } (RN-1.2).
            .Produces<ErroDto>(StatusCodes.Status401Unauthorized)
            // Dois 400 com corpos diferentes; a SPA escolhe o tratamento pela forma do corpo:
            // ValidationProblem (errors.Usuario/errors.Senha) e { mensagem } do CSRF (token ausente ou vencido).
            .ProducesValidationProblem()
            .Produces<ErroDto>(StatusCodes.Status400BadRequest)
            // Limite por IP (story 1-10): 429 { mensagem } com Retry-After, sem chegar ao caso de uso.
            .Produces<ErroDto>(StatusCodes.Status429TooManyRequests);

        grupo.MapPost("/logout", Sair)
            .WithName("Sair")
            .WithSummary("Encerra a sessão")
            .Produces(StatusCodes.Status204NoContent)
            // Sessão já vencida: o token XSRF era da identidade anterior → 400 { mensagem } do CSRF.
            .Produces<ErroDto>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .LiberarNaTrocaDeSenha(); // com a senha provisória, ainda dá para sair

        grupo.MapGet("/me", ObterSessaoAtual)
            .WithName("ObterSessaoAtual")
            .WithSummary("Usuário da sessão, com perfil, permissões e se a troca de senha é obrigatória")
            .Produces<SessaoDto>()
            .Produces(StatusCodes.Status401Unauthorized)
            .LiberarNaTrocaDeSenha();

        grupo.MapPost("/trocar-senha", TrocarSenha)
            .WithName("TrocarSenha")
            .WithSummary("O usuário da sessão troca a própria senha (obrigatório com a senha provisória, RN-1.8)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .LiberarNaTrocaDeSenha();

        return app;
    }

    private static async Task<IResult> TrocarSenha(TrocarSenhaRequest request, ISender mediator, HttpContext contexto, CancellationToken ct)
    {
        var resultado = await mediator.Send(new TrocarSenhaCommand(request.SenhaAtual ?? string.Empty, request.NovaSenha ?? string.Empty), ct);
        if (resultado.IsSuccess)
            ProtecaoCsrf.EmitirToken(contexto); // a sessão foi renovada: token ligado à identidade nova
        return resultado.ParaHttp();
    }

    private static async Task<IResult> Entrar(EntrarRequest request, ISender mediator, HttpContext contexto, CancellationToken ct)
    {
        var resultado = await mediator.Send(new EntrarCommand(request.Usuario ?? string.Empty, request.Senha ?? string.Empty), ct);

        if (resultado.IsSuccess)
            ProtecaoCsrf.EmitirToken(contexto); // token novo, ligado ao usuário que entrou

        // Bloqueio por tentativas: 401 como os outros erros de login, com código para a SPA (decisão de 06/11/2026).
        if (resultado.Status == ResultStatus.Unauthorized && resultado.Errors.Contains(EntrarCommand.MensagemBloqueado))
            return Results.Json(new ErroDto(EntrarCommand.MensagemBloqueado, CodigoBloqueado), statusCode: StatusCodes.Status401Unauthorized);

        return resultado.ParaHttp();
    }

    private static async Task<IResult> Sair(ISender mediator, HttpContext contexto, CancellationToken ct)
    {
        var resultado = await mediator.Send(new SairCommand(), ct);
        ProtecaoCsrf.EmitirToken(contexto); // token anônimo para o próximo login
        return resultado.ParaHttp();
    }

    private static async Task<IResult> ObterSessaoAtual(ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new ObterSessaoAtualQuery(), ct)).ParaHttp();
}
