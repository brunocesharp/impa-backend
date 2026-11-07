using Impa.Host.Http;
using Impa.Host.Seguranca;
using Impa.Modules.Acesso.Application.Usuarios.Commands.DesativarUsuario;
using Impa.Modules.Acesso.Contracts;
using MediatR;

namespace Impa.Host.Endpoints;

/// <summary><c>/api/usuarios</c>: gestão de usuários. Nesta fase só a desativação (story 1-2); a tela vem na 1.4.</summary>
public static class UsuariosEndpoints
{
    public static IEndpointRouteBuilder MapUsuariosEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/usuarios")
            .WithTags("Usuários")
            .RequirePermissao(Permissoes.UsuariosGerenciar);

        grupo.MapPost("/{id:guid}/desativar", Desativar)
            .WithName("DesativarUsuario")
            .WithSummary("Desativa o usuário e encerra as sessões abertas dele (exige usuarios.gerenciar)")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErroDto>(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> Desativar(Guid id, ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new DesativarUsuarioCommand(id), ct)).ParaHttp();
}
