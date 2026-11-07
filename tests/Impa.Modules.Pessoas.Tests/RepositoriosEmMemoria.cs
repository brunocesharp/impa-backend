using Impa.Modules.Pessoas.Application.Clientes;
using Impa.Modules.Pessoas.Application.Comum;
using Impa.Modules.Pessoas.Application.Fornecedores;
using Impa.Modules.Pessoas.Contracts;
using Impa.Modules.Pessoas.Domain;

namespace Impa.Modules.Pessoas.Tests;

internal sealed class ClientesEmMemoria : IClientesRepository
{
    public List<Cliente> Itens { get; } = [];

    public Task<Cliente?> ObterAsync(Guid id, CancellationToken ct) => Task.FromResult(Itens.FirstOrDefault(c => c.Id == id));

    public Task<Cliente?> ObterPorDocumentoAsync(string documento, Guid? exceto, CancellationToken ct) =>
        Task.FromResult(Itens.FirstOrDefault(c => c.Documento == documento && c.Id != exceto));

    public Task<IReadOnlyList<Cliente>> BuscarAsync(TermosDeBusca termos, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Cliente>>(Itens.Where(c => termos.IncluirInativos || c.Ativo).ToList());

    public Task<bool> AdicionarAsync(Cliente cliente, CancellationToken ct)
    {
        Itens.Add(cliente);
        return Task.FromResult(true);
    }

    public Task<bool> SalvarAsync(CancellationToken ct) => Task.FromResult(true);

    public Task RemoverAsync(Cliente cliente, CancellationToken ct)
    {
        Itens.Remove(cliente);
        return Task.CompletedTask;
    }
}

internal sealed class FornecedoresEmMemoria : IFornecedoresRepository
{
    public List<Fornecedor> Itens { get; } = [];

    public Task<Fornecedor?> ObterAsync(Guid id, CancellationToken ct) => Task.FromResult(Itens.FirstOrDefault(f => f.Id == id));

    public Task<Fornecedor?> ObterPorDocumentoAsync(string documento, Guid? exceto, CancellationToken ct) =>
        Task.FromResult(Itens.FirstOrDefault(f => f.Documento == documento && f.Id != exceto));

    public Task<IReadOnlyList<Fornecedor>> BuscarAsync(TermosDeBusca termos, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Fornecedor>>(Itens.Where(f => termos.IncluirInativos || f.Ativo).ToList());

    public Task<bool> AdicionarAsync(Fornecedor fornecedor, CancellationToken ct)
    {
        Itens.Add(fornecedor);
        return Task.FromResult(true);
    }

    public Task<bool> SalvarAsync(CancellationToken ct) => Task.FromResult(true);

    public Task RemoverAsync(Fornecedor fornecedor, CancellationToken ct)
    {
        Itens.Remove(fornecedor);
        return Task.CompletedTask;
    }
}

/// <summary>Dublê da porta de histórico (as implementações reais vêm nos épicos 6, 11 e 12).</summary>
internal sealed class HistoricoFixo(params Guid[] comHistorico) : IHistoricoDaPessoa
{
    public Task<bool> TemHistoricoAsync(TipoDePessoa tipo, Guid pessoaId, CancellationToken ct) =>
        Task.FromResult(comHistorico.Contains(pessoaId));
}
