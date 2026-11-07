using Impa.Host.Http;
using Impa.Host.Seguranca;
using Impa.Modules.Acesso.Application.Perfis;
using Impa.Modules.Acesso.Application.Perfis.Commands.AlterarPerfil;
using Impa.Modules.Acesso.Application.Perfis.Commands.CriarPerfil;
using Impa.Modules.Acesso.Application.Perfis.Commands.ExcluirPerfil;
using Impa.Modules.Acesso.Application.Perfis.Queries.ListarPerfis;
using Impa.Modules.Acesso.Contracts;
using MediatR;

namespace Impa.Host.Endpoints;

public sealed record PerfilRequest(string? Nome, IReadOnlyList<string>? Permissoes);

/// <summary><c>/api/perfis</c>: perfis e permissões (story 1.3, RN-1.4 a RN-1.6). Exige <c>perfis.gerenciar</c>.</summary>
public static class PerfisEndpoints
{
    public static IEndpointRouteBuilder MapPerfisEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/perfis")
            .WithTags("Perfis")
            .RequirePermissao(Permissoes.PerfisGerenciar); // sem sessão 401; sem a permissão 403

        grupo.MapGet("/", Listar)
            .WithName("ListarPerfis")
            .WithSummary("Perfis com as permissões e o total de usuários (Administrador primeiro)")
            .Produces<IReadOnlyList<PerfilDto>>();

        grupo.MapGet("/permissoes", () => Results.Ok(Permissoes.Catalogo))
            .WithName("ListarPermissoes")
            .WithSummary("Catálogo de permissões, na ordem e nos grupos da tela")
            .Produces<IReadOnlyList<PermissaoInfo>>();

        grupo.MapPost("/", Criar)
            .WithName("CriarPerfil")
            .WithSummary("Cria um perfil com as permissões indicadas")
            .Produces<PerfilDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces<ErroDto>(StatusCodes.Status409Conflict);

        grupo.MapPut("/{id:guid}", Alterar)
            .WithName("AlterarPerfil")
            .WithSummary("Troca nome e permissões; o Administrador não pode ser alterado (409)")
            .Produces<PerfilDto>()
            .ProducesValidationProblem()
            .Produces<ErroDto>(StatusCodes.Status404NotFound)
            .Produces<ErroDto>(StatusCodes.Status409Conflict);

        grupo.MapDelete("/{id:guid}", Excluir)
            .WithName("ExcluirPerfil")
            .WithSummary("Exclui um perfil sem usuários; o Administrador não pode ser excluído (409)")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErroDto>(StatusCodes.Status404NotFound)
            .Produces<ErroDto>(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> Listar(ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new ListarPerfisQuery(), ct)).ParaHttp();

    private static async Task<IResult> Criar(PerfilRequest request, ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new CriarPerfilCommand(request.Nome ?? string.Empty, request.Permissoes ?? []), ct)).ParaHttp();

    private static async Task<IResult> Alterar(Guid id, PerfilRequest request, ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new AlterarPerfilCommand(id, request.Nome ?? string.Empty, request.Permissoes ?? []), ct)).ParaHttp();

    private static async Task<IResult> Excluir(Guid id, ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new ExcluirPerfilCommand(id), ct)).ParaHttp();
}
