using Impa.Modules.Financeiro.Application.Categorias;
using Impa.Modules.Financeiro.Application.Contas;
using Impa.Modules.Financeiro.Domain;
using Impa.Modules.Pessoas.Contracts;

namespace Impa.Modules.Financeiro.Tests;

internal sealed class CategoriasEmMemoria : ICategoriasRepository
{
    public List<CategoriaFinanceira> Itens { get; } = [];

    public Task<CategoriaFinanceira?> ObterAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(Itens.FirstOrDefault(c => c.Id == id));

    public Task<CategoriaFinanceira?> ObterPorNomeAsync(string nome, Guid? exceto, CancellationToken ct) =>
        Task.FromResult(Itens.FirstOrDefault(c =>
            string.Equals(c.Nome, nome, StringComparison.OrdinalIgnoreCase) && c.Id != exceto));

    public Task<IReadOnlyList<CategoriaFinanceira>> ListarAsync(bool incluirInativas, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CategoriaFinanceira>>(
            Itens.Where(c => incluirInativas || c.Ativa).OrderBy(c => c.Nome).ToList());

    public Task<bool> AdicionarAsync(CategoriaFinanceira categoria, CancellationToken ct)
    {
        Itens.Add(categoria);
        return Task.FromResult(true);
    }

    public Task<bool> SalvarAsync(CancellationToken ct) => Task.FromResult(true);

    public Task RemoverAsync(CategoriaFinanceira categoria, CancellationToken ct)
    {
        Itens.Remove(categoria);
        return Task.CompletedTask;
    }
}

internal sealed class ContasEmMemoria : IContasRepository
{
    public List<Conta> Itens { get; } = [];

    public Task<Conta?> ObterAsync(Guid id, CancellationToken ct) => Task.FromResult(Itens.FirstOrDefault(c => c.Id == id));

    public Task<IReadOnlyList<Conta>> ListarAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Conta>>(Itens.OrderBy(c => c.Vencimento).ToList());

    public Task AdicionarAsync(IReadOnlyList<Conta> contas, CancellationToken ct)
    {
        Itens.AddRange(contas);
        return Task.CompletedTask;
    }

    public Task SalvarAsync(CancellationToken ct) => Task.CompletedTask;

    public Task RemoverAsync(Conta conta, CancellationToken ct)
    {
        Itens.Remove(conta);
        return Task.CompletedTask;
    }

    public Task<bool> ExisteDaCategoriaAsync(Guid categoriaId, CancellationToken ct) =>
        Task.FromResult(Itens.Any(c => c.CategoriaId == categoriaId));

    public Task<bool> ExisteDoFornecedorAsync(Guid fornecedorId, CancellationToken ct) =>
        Task.FromResult(Itens.Any(c => c.FornecedorId == fornecedorId));
}

/// <summary>Dublê do contrato do módulo Pessoas (3.7): só os fornecedores registrados aqui existem.</summary>
internal sealed class PessoasFixas : IPessoasConsultas
{
    public List<FornecedorResumo> Fornecedores { get; } = [];

    public Task<IReadOnlyList<ClienteResumo>> BuscarClientesAsync(string busca, int limite, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ClienteResumo>>([]);

    public Task<ClienteResumo?> ObterClienteAsync(Guid clienteId, CancellationToken ct) => Task.FromResult<ClienteResumo?>(null);

    public Task<FornecedorResumo?> ObterFornecedorPorDocumentoAsync(string documento, CancellationToken ct) =>
        Task.FromResult(Fornecedores.FirstOrDefault(f => f.Documento == Impa.Shared.Documento.SomenteDigitos(documento)));
}
