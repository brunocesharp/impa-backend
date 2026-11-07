using Impa.Host.Http;
using Impa.Host.Seguranca;
using Impa.Modules.Acesso.Contracts;
using Impa.Modules.Pessoas.Application.Clientes;
using Impa.Modules.Pessoas.Application.Clientes.Commands;
using Impa.Modules.Pessoas.Application.Clientes.Queries;
using Impa.Modules.Pessoas.Application.Fornecedores;
using Impa.Modules.Pessoas.Application.Fornecedores.Commands;
using Impa.Modules.Pessoas.Application.Fornecedores.Queries;
using MediatR;

namespace Impa.Host.Endpoints;

/// <summary>Corpo de cadastrar e alterar cliente (RN-14.1). <c>Documento</c>: CPF ou CNPJ, com ou sem máscara, opcional.</summary>
public sealed record ClienteRequest(
    string? Nome, string? Documento, string? Telefone, string? Email, string? Endereco, string? Observacoes)
{
    // RN-14.6: sem dados pessoais em log.
    public override string ToString() => $"{nameof(ClienteRequest)} {{ *** }}";
}

/// <summary>Corpo de cadastrar e alterar fornecedor (RN-14.2). <c>Documento</c>: CNPJ ou CPF, obrigatório.</summary>
public sealed record FornecedorRequest(
    string? RazaoSocial, string? Documento, string? NomeFantasia, string? Telefone, string? Email, string? Contato)
{
    public override string ToString() => $"{nameof(FornecedorRequest)} {{ *** }}";
}

/// <summary>
/// <c>/api/pessoas/clientes</c> e <c>/api/pessoas/fornecedores</c> (story 3-7, RN-14.5): o operador de caixa cadastra
/// e consulta clientes; só quem tem <c>clientes.inativar</c> inativa, reativa ou exclui; fornecedores, tudo com
/// <c>fornecedores.gerenciar</c>. Respostas com dados pessoais só com <c>clientes.ver</c> (RN-14.6).
/// </summary>
public static class PessoasEndpoints
{
    public static IEndpointRouteBuilder MapPessoasEndpoints(this IEndpointRouteBuilder app)
    {
        MapClientes(app.MapGroup("/api/pessoas/clientes").WithTags("Clientes"));
        MapFornecedores(app.MapGroup("/api/pessoas/fornecedores").WithTags("Fornecedores")
            .RequirePermissao(Permissoes.FornecedoresGerenciar));
        return app;
    }

    private static void MapClientes(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/", ListarClientes)
            .RequirePermissao(Permissoes.ClientesVer)
            .WithName("ListarClientes")
            .WithSummary("Clientes pela busca (nome, CPF/CNPJ ou telefone); só ativos, salvo incluirInativos=true")
            .Produces<IReadOnlyList<ClienteDto>>();

        grupo.MapGet("/{id:guid}", ObterCliente)
            .RequirePermissao(Permissoes.ClientesVer)
            .WithName("ObterCliente")
            .Produces<ClienteDto>()
            .Produces<ErroDto>(StatusCodes.Status404NotFound);

        // A resposta traz os dados pessoais: exige também clientes.ver (RN-14.6).
        grupo.MapPost("/", CadastrarCliente)
            .RequirePermissao(Permissoes.ClientesVer)
            .RequirePermissao(Permissoes.ClientesEditar)
            .WithName("CadastrarCliente")
            .WithSummary("Cadastra o cliente; CPF/CNPJ opcional, mas válido e único (cenários 14.1 e 14.2)")
            .Produces<ClienteDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces<ErroDto>(StatusCodes.Status409Conflict);

        grupo.MapPut("/{id:guid}", AlterarCliente)
            .RequirePermissao(Permissoes.ClientesVer)
            .RequirePermissao(Permissoes.ClientesEditar)
            .WithName("AlterarCliente")
            .Produces<ClienteDto>()
            .ProducesValidationProblem()
            .Produces<ErroDto>(StatusCodes.Status404NotFound)
            .Produces<ErroDto>(StatusCodes.Status409Conflict);

        grupo.MapPost("/{id:guid}/inativar", InativarCliente)
            .RequirePermissao(Permissoes.ClientesInativar)
            .WithName("InativarCliente")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErroDto>(StatusCodes.Status404NotFound);

        grupo.MapPost("/{id:guid}/reativar", ReativarCliente)
            .RequirePermissao(Permissoes.ClientesInativar)
            .WithName("ReativarCliente")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErroDto>(StatusCodes.Status404NotFound);

        grupo.MapDelete("/{id:guid}", ExcluirCliente)
            .RequirePermissao(Permissoes.ClientesInativar)
            .WithName("ExcluirCliente")
            .WithSummary("Exclui o cliente sem histórico; com vendas, notas ou contas: 409 (cenário 14.3)")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErroDto>(StatusCodes.Status404NotFound)
            .Produces<ErroDto>(StatusCodes.Status409Conflict);
    }

    private static void MapFornecedores(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/", ListarFornecedores)
            .WithName("ListarFornecedores")
            .WithSummary("Fornecedores pela busca (razão social, nome fantasia ou CNPJ); só ativos, salvo incluirInativos=true")
            .Produces<IReadOnlyList<FornecedorDto>>();

        grupo.MapGet("/{id:guid}", ObterFornecedor)
            .WithName("ObterFornecedor")
            .Produces<FornecedorDto>()
            .Produces<ErroDto>(StatusCodes.Status404NotFound);

        grupo.MapPost("/", CadastrarFornecedor)
            .WithName("CadastrarFornecedor")
            .WithSummary("Cadastra o fornecedor; razão social e CNPJ/CPF obrigatórios, documento válido e único")
            .Produces<FornecedorDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces<ErroDto>(StatusCodes.Status409Conflict);

        grupo.MapPut("/{id:guid}", AlterarFornecedor)
            .WithName("AlterarFornecedor")
            .Produces<FornecedorDto>()
            .ProducesValidationProblem()
            .Produces<ErroDto>(StatusCodes.Status404NotFound)
            .Produces<ErroDto>(StatusCodes.Status409Conflict);

        grupo.MapPost("/{id:guid}/inativar", InativarFornecedor)
            .WithName("InativarFornecedor")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErroDto>(StatusCodes.Status404NotFound);

        grupo.MapPost("/{id:guid}/reativar", ReativarFornecedor)
            .WithName("ReativarFornecedor")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErroDto>(StatusCodes.Status404NotFound);

        grupo.MapDelete("/{id:guid}", ExcluirFornecedor)
            .WithName("ExcluirFornecedor")
            .WithSummary("Exclui o fornecedor sem histórico; com notas ou contas: 409 (RN-14.4)")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErroDto>(StatusCodes.Status404NotFound)
            .Produces<ErroDto>(StatusCodes.Status409Conflict);
    }

    // ---- Clientes ----

    private static async Task<IResult> ListarClientes(string? busca, bool? incluirInativos, int? limite,
        ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new ListarClientesQuery(busca, incluirInativos ?? false, limite), ct)).ParaHttp();

    private static async Task<IResult> ObterCliente(Guid id, ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new ObterClienteQuery(id), ct)).ParaHttp();

    private static async Task<IResult> CadastrarCliente(ClienteRequest request, ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new CadastrarClienteCommand(request.Nome ?? string.Empty, request.Documento,
            request.Telefone, request.Email, request.Endereco, request.Observacoes), ct)).ParaHttp();

    private static async Task<IResult> AlterarCliente(Guid id, ClienteRequest request, ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new AlterarClienteCommand(id, request.Nome ?? string.Empty, request.Documento,
            request.Telefone, request.Email, request.Endereco, request.Observacoes), ct)).ParaHttp();

    private static async Task<IResult> InativarCliente(Guid id, ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new InativarClienteCommand(id), ct)).ParaHttp();

    private static async Task<IResult> ReativarCliente(Guid id, ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new ReativarClienteCommand(id), ct)).ParaHttp();

    private static async Task<IResult> ExcluirCliente(Guid id, ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new ExcluirClienteCommand(id), ct)).ParaHttp();

    // ---- Fornecedores ----

    private static async Task<IResult> ListarFornecedores(string? busca, bool? incluirInativos, int? limite,
        ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new ListarFornecedoresQuery(busca, incluirInativos ?? false, limite), ct)).ParaHttp();

    private static async Task<IResult> ObterFornecedor(Guid id, ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new ObterFornecedorQuery(id), ct)).ParaHttp();

    private static async Task<IResult> CadastrarFornecedor(FornecedorRequest request, ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new CadastrarFornecedorCommand(request.RazaoSocial ?? string.Empty, request.Documento,
            request.NomeFantasia, request.Telefone, request.Email, request.Contato), ct)).ParaHttp();

    private static async Task<IResult> AlterarFornecedor(Guid id, FornecedorRequest request, ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new AlterarFornecedorCommand(id, request.RazaoSocial ?? string.Empty, request.Documento,
            request.NomeFantasia, request.Telefone, request.Email, request.Contato), ct)).ParaHttp();

    private static async Task<IResult> InativarFornecedor(Guid id, ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new InativarFornecedorCommand(id), ct)).ParaHttp();

    private static async Task<IResult> ReativarFornecedor(Guid id, ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new ReativarFornecedorCommand(id), ct)).ParaHttp();

    private static async Task<IResult> ExcluirFornecedor(Guid id, ISender mediator, CancellationToken ct) =>
        (await mediator.Send(new ExcluirFornecedorCommand(id), ct)).ParaHttp();
}
