using Impa.Host.Http;
using Impa.Host.Seguranca;
using Impa.Modules.Acesso.Contracts;
using Impa.Modules.Financeiro.Application.Categorias;
using Impa.Modules.Financeiro.Application.Categorias.Commands;
using Impa.Modules.Financeiro.Application.Categorias.Queries;
using Impa.Modules.Financeiro.Application.Contas;
using Impa.Modules.Financeiro.Application.Contas.Commands;
using Impa.Modules.Financeiro.Application.Contas.Queries;
using MediatR;

namespace Impa.Host.Endpoints;

/// <summary>Corpo de criar e renomear categoria financeira (RN-23.1).</summary>
public sealed record CategoriaFinanceiraRequest(string? Nome);

/// <summary>
/// Corpo de lançar conta (RN-23.2, RN-23.3): <c>Valor</c> é o total e <c>Vencimento</c> o primeiro vencimento
/// (<c>AAAA-MM-DD</c>); <c>Parcelas</c> padrão 1. <c>FornecedorDocumento</c>: CNPJ/CPF de um fornecedor cadastrado, opcional.
/// </summary>
public sealed record LancarContaRequest(
    string? Descricao,
    Guid? CategoriaId,
    decimal? Valor,
    DateOnly? Vencimento,
    int? Parcelas,
    string? FornecedorDocumento,
    string? NumeroDocumento,
    string? Observacao);

/// <summary>Corpo de alterar uma conta (parcela).</summary>
public sealed record AlterarContaRequest(
    string? Descricao,
    Guid? CategoriaId,
    decimal? Valor,
    DateOnly? Vencimento,
    string? FornecedorDocumento,
    string? NumeroDocumento,
    string? Observacao);

/// <summary>
/// <c>/api/financeiro/categorias</c> e <c>/api/financeiro/contas</c> (story 11-1): consultar com <c>financeiro.ver</c>;
/// lançar, alterar e excluir com <c>financeiro.editar</c> (decisão de 07/11/2026; só o Administrador).
/// </summary>
public static class FinanceiroEndpoints
{
    public static IEndpointRouteBuilder MapFinanceiroEndpoints(this IEndpointRouteBuilder app)
    {
        var financeiro = app.MapGroup("/api/financeiro").RequirePermissao(Permissoes.FinanceiroVer);
        MapCategorias(financeiro.MapGroup("/categorias").WithTags("Categorias financeiras"));
        MapContas(financeiro.MapGroup("/contas").WithTags("Contas a pagar"));
        return app;
    }

    private static void MapCategorias(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/", ListarCategorias)
            .WithName("ListarCategoriasFinanceiras")
            .WithSummary("Categorias em ordem de nome; só ativas, salvo incluirInativas=true")
            .Produces<IReadOnlyList<CategoriaDto>>();

        grupo.MapPost("/", CriarCategoria)
            .RequirePermissao(Permissoes.FinanceiroEditar)
            .WithName("CriarCategoriaFinanceira")
            .Produces<CategoriaDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces<ErroDto>(StatusCodes.Status409Conflict);

        grupo.MapPut("/{id:guid}", AlterarCategoria)
            .RequirePermissao(Permissoes.FinanceiroEditar)
            .WithName("AlterarCategoriaFinanceira")
            .Produces<CategoriaDto>()
            .ProducesValidationProblem()
            .Produces<ErroDto>(StatusCodes.Status404NotFound)
            .Produces<ErroDto>(StatusCodes.Status409Conflict);

        grupo.MapPost("/{id:guid}/inativar", InativarCategoria)
            .RequirePermissao(Permissoes.FinanceiroEditar)
            .WithName("InativarCategoriaFinanceira")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErroDto>(StatusCodes.Status404NotFound);

        grupo.MapPost("/{id:guid}/reativar", ReativarCategoria)
            .RequirePermissao(Permissoes.FinanceiroEditar)
            .WithName("ReativarCategoriaFinanceira")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErroDto>(StatusCodes.Status404NotFound);

        grupo.MapDelete("/{id:guid}", ExcluirCategoria)
            .RequirePermissao(Permissoes.FinanceiroEditar)
            .WithName("ExcluirCategoriaFinanceira")
            .WithSummary("Exclui a categoria sem contas; com contas: 409, só inativar (RN-23.1)")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErroDto>(StatusCodes.Status404NotFound)
            .Produces<ErroDto>(StatusCodes.Status409Conflict);
    }

    private static void MapContas(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/", ListarContas)
            .WithName("ListarContas")
            .WithSummary("Contas (uma por parcela) em ordem de vencimento")
            .Produces<IReadOnlyList<ContaDto>>();

        grupo.MapGet("/{id:guid}", ObterConta)
            .WithName("ObterConta")
            .Produces<ContaDto>()
            .Produces<ErroDto>(StatusCodes.Status404NotFound);

        grupo.MapPost("/", LancarConta)
            .RequirePermissao(Permissoes.FinanceiroEditar)
            .WithName("LancarConta")
            .WithSummary("Lança a conta em n parcelas; devolve as parcelas criadas (cenário 23.1, RN-23.3)")
            .Produces<IReadOnlyList<ContaDto>>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        grupo.MapPut("/{id:guid}", AlterarConta)
            .RequirePermissao(Permissoes.FinanceiroEditar)
            .WithName("AlterarConta")
            .Produces<ContaDto>()
            .ProducesValidationProblem()
            .Produces<ErroDto>(StatusCodes.Status404NotFound);

        grupo.MapDelete("/{id:guid}", ExcluirConta)
            .RequirePermissao(Permissoes.FinanceiroEditar)
            .WithName("ExcluirConta")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErroDto>(StatusCodes.Status404NotFound);
    }

    // ---- Categorias ----

    private static async Task<IResult> ListarCategorias(bool? incluirInativas, ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new ListarCategoriasQuery(incluirInativas ?? false), ct)).ParaHttp();

    private static async Task<IResult> CriarCategoria(CategoriaFinanceiraRequest request, ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new CriarCategoriaCommand(request.Nome ?? string.Empty), ct)).ParaHttp();

    private static async Task<IResult> AlterarCategoria(Guid id, CategoriaFinanceiraRequest request, ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new AlterarCategoriaCommand(id, request.Nome ?? string.Empty), ct)).ParaHttp();

    private static async Task<IResult> InativarCategoria(Guid id, ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new InativarCategoriaCommand(id), ct)).ParaHttp();

    private static async Task<IResult> ReativarCategoria(Guid id, ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new ReativarCategoriaCommand(id), ct)).ParaHttp();

    private static async Task<IResult> ExcluirCategoria(Guid id, ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new ExcluirCategoriaCommand(id), ct)).ParaHttp();

    // ---- Contas ----

    private static async Task<IResult> ListarContas(ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new ListarContasQuery(), ct)).ParaHttp();

    private static async Task<IResult> ObterConta(Guid id, ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new ObterContaQuery(id), ct)).ParaHttp();

    private static async Task<IResult> LancarConta(LancarContaRequest request, ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new LancarContaCommand(
            request.Descricao ?? string.Empty, request.CategoriaId ?? Guid.Empty, request.Valor ?? 0m, request.Vencimento,
            request.Parcelas ?? 1, request.FornecedorDocumento, request.NumeroDocumento, request.Observacao), ct)).ParaHttp();

    private static async Task<IResult> AlterarConta(Guid id, AlterarContaRequest request, ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new AlterarContaCommand(
            id, request.Descricao ?? string.Empty, request.CategoriaId ?? Guid.Empty, request.Valor ?? 0m, request.Vencimento,
            request.FornecedorDocumento, request.NumeroDocumento, request.Observacao), ct)).ParaHttp();

    private static async Task<IResult> ExcluirConta(Guid id, ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new ExcluirContaCommand(id), ct)).ParaHttp();
}
