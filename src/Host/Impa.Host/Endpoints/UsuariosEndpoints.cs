using Impa.Host.Http;
using Impa.Host.Seguranca;
using Impa.Modules.Acesso.Application.Usuarios;
using Impa.Modules.Acesso.Application.Usuarios.Commands.AlterarUsuario;
using Impa.Modules.Acesso.Application.Usuarios.Commands.CriarUsuario;
using Impa.Modules.Acesso.Application.Usuarios.Commands.DesativarUsuario;
using Impa.Modules.Acesso.Application.Usuarios.Commands.ReativarUsuario;
using Impa.Modules.Acesso.Application.Usuarios.Commands.RedefinirSenha;
using Impa.Modules.Acesso.Application.Usuarios.Queries.ListarPerfisDosUsuarios;
using Impa.Modules.Acesso.Application.Usuarios.Queries.ListarUsuarios;
using Impa.Modules.Acesso.Contracts;
using MediatR;

namespace Impa.Host.Endpoints;

/// <summary>Usuário novo: <paramref name="Senha"/> é a inicial provisória; sem ela, o sistema gera.</summary>
public sealed record CriarUsuarioRequest(string? Usuario, string? Nome, string? Email, Guid? PerfilId, string? Senha)
{
    public override string ToString() => $"{nameof(CriarUsuarioRequest)} {{ Usuario = {Usuario}, Senha = *** }}";
}

public sealed record AlterarUsuarioRequest(string? Nome, string? Email, Guid? PerfilId);

/// <summary>Corpo opcional: sem <paramref name="Senha"/>, o sistema gera a provisória.</summary>
public sealed record RedefinirSenhaRequest(string? Senha)
{
    public override string ToString() => $"{nameof(RedefinirSenhaRequest)} {{ Senha = *** }}";
}

/// <summary><c>/api/usuarios</c>: gestão de usuários (stories 1-2 e 1.4). Tudo exige <c>usuarios.gerenciar</c>.</summary>
public static class UsuariosEndpoints
{
    public static IEndpointRouteBuilder MapUsuariosEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/usuarios")
            .WithTags("Usuários")
            .RequirePermissao(Permissoes.UsuariosGerenciar); // sem sessão 401; sem a permissão 403

        grupo.MapGet("/", Listar)
            .WithName("ListarUsuarios")
            .WithSummary("Usuários, ativos e inativos, com perfil e situação")
            .Produces<IReadOnlyList<UsuarioDto>>();

        grupo.MapGet("/perfis", ListarPerfis)
            .WithName("ListarPerfisDosUsuarios")
            .WithSummary("Perfis para escolher na tela Usuários: só id, nome e protegido")
            .Produces<IReadOnlyList<PerfilResumoDto>>();

        grupo.MapPost("/", Criar)
            .WithName("CriarUsuario")
            .WithSummary("Cria o usuário com perfil e senha inicial provisória (informada ou gerada), mostrada só nesta resposta")
            .Produces<SenhaProvisoriaDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces<ErroDto>(StatusCodes.Status404NotFound)
            .Produces<ErroDto>(StatusCodes.Status409Conflict);

        grupo.MapPut("/{id:guid}", Alterar)
            .WithName("AlterarUsuario")
            .WithSummary("Troca nome, e-mail e perfil; o login não muda")
            .Produces<UsuarioDto>()
            .ProducesValidationProblem()
            .Produces<ErroDto>(StatusCodes.Status404NotFound)
            .Produces<ErroDto>(StatusCodes.Status409Conflict);

        grupo.MapPost("/{id:guid}/desativar", Desativar)
            .WithName("DesativarUsuario")
            .WithSummary("Desativa o usuário e encerra as sessões abertas dele")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErroDto>(StatusCodes.Status404NotFound)
            .Produces<ErroDto>(StatusCodes.Status409Conflict);

        grupo.MapPost("/{id:guid}/reativar", Reativar)
            .WithName("ReativarUsuario")
            .WithSummary("Reativa um usuário desativado")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErroDto>(StatusCodes.Status404NotFound);

        grupo.MapPost("/{id:guid}/redefinir-senha", RedefinirSenha)
            .WithName("RedefinirSenhaDoUsuario")
            .WithSummary("Troca a senha por uma provisória (informada ou gerada) e obriga a troca no próximo acesso (RN-1.8)")
            .Produces<SenhaProvisoriaDto>()
            .ProducesValidationProblem()
            .Produces<ErroDto>(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> Listar(ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new ListarUsuariosQuery(), ct)).ParaHttp();

    private static async Task<IResult> ListarPerfis(ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new ListarPerfisDosUsuariosQuery(), ct)).ParaHttp();

    private static async Task<IResult> Criar(CriarUsuarioRequest request, ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new CriarUsuarioCommand(request.Usuario ?? string.Empty, request.Nome ?? string.Empty,
            request.Email, request.PerfilId ?? Guid.Empty, request.Senha), ct)).ParaHttp();

    private static async Task<IResult> Alterar(Guid id, AlterarUsuarioRequest request, ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new AlterarUsuarioCommand(id, request.Nome ?? string.Empty, request.Email,
            request.PerfilId ?? Guid.Empty), ct)).ParaHttp();

    private static async Task<IResult> Desativar(Guid id, ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new DesativarUsuarioCommand(id), ct)).ParaHttp();

    private static async Task<IResult> Reativar(Guid id, ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new ReativarUsuarioCommand(id), ct)).ParaHttp();

    private static async Task<IResult> RedefinirSenha(Guid id, RedefinirSenhaRequest? request, ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new RedefinirSenhaCommand(id, request?.Senha), ct)).ParaHttp();
}
