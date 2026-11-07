using Ardalis.Result;
using Impa.Modules.Pessoas.Application.Comum;
using Impa.Modules.Pessoas.Contracts;
using MediatR;

namespace Impa.Modules.Pessoas.Application.Clientes.Commands;

/// <summary>Inativa o cliente: some da busca do PDV, mas o histórico fica (RN-14.4). Idempotente.</summary>
public sealed record InativarClienteCommand(Guid ClienteId) : IRequest<Result>;

public sealed record ReativarClienteCommand(Guid ClienteId) : IRequest<Result>;

/// <summary>Cenário 14.3 (RN-14.4): só exclui cliente sem vendas, notas nem contas vinculadas.</summary>
public sealed record ExcluirClienteCommand(Guid ClienteId) : IRequest<Result>;

public sealed class InativarClienteCommandHandler(IClientesRepository clientes) : IRequestHandler<InativarClienteCommand, Result>
{
    public async Task<Result> Handle(InativarClienteCommand request, CancellationToken ct)
    {
        if (await clientes.ObterAsync(request.ClienteId, ct) is not { } cliente)
            return Result.NotFound(MensagensPessoas.ClienteNaoEncontrado);

        cliente.Inativar();
        await clientes.SalvarAsync(ct);
        return Result.Success();
    }
}

public sealed class ReativarClienteCommandHandler(IClientesRepository clientes) : IRequestHandler<ReativarClienteCommand, Result>
{
    public async Task<Result> Handle(ReativarClienteCommand request, CancellationToken ct)
    {
        if (await clientes.ObterAsync(request.ClienteId, ct) is not { } cliente)
            return Result.NotFound(MensagensPessoas.ClienteNaoEncontrado);

        cliente.Reativar();
        await clientes.SalvarAsync(ct);
        return Result.Success();
    }
}

public sealed class ExcluirClienteCommandHandler(IClientesRepository clientes, IEnumerable<IHistoricoDaPessoa> historicos)
    : IRequestHandler<ExcluirClienteCommand, Result>
{
    public async Task<Result> Handle(ExcluirClienteCommand request, CancellationToken ct)
    {
        if (await clientes.ObterAsync(request.ClienteId, ct) is not { } cliente)
            return Result.NotFound(MensagensPessoas.ClienteNaoEncontrado);

        if (await HistoricoDasPessoas.TemAsync(historicos, TipoDePessoa.Cliente, cliente.Id, ct))
            return Result.Conflict(MensagensPessoas.ClienteComHistorico);

        await clientes.RemoverAsync(cliente, ct);
        return Result.Success();
    }
}
