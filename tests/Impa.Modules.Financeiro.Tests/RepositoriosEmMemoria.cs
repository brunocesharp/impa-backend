using Impa.Modules.Financeiro.Application.Categorias;
using Impa.Modules.Financeiro.Application.Contas;
using Impa.Modules.Financeiro.Application.FormasDePagamento;
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

    /// <summary>Simula a chave estrangeira recusando a exclusão (23503).</summary>
    public bool RecusarRemocao { get; set; }

    public Task<bool> RemoverAsync(CategoriaFinanceira categoria, CancellationToken ct)
    {
        if (RecusarRemocao)
            return Task.FromResult(false);
        Itens.Remove(categoria);
        return Task.FromResult(true);
    }
}

internal sealed class ContasEmMemoria : IContasRepository
{
    public List<Conta> Itens { get; } = [];

    public Task<Conta?> ObterAsync(Guid id, CancellationToken ct) => Task.FromResult(Itens.FirstOrDefault(c => c.Id == id));

    public Task<Conta?> ObterPelaBaixaAsync(Guid baixaId, CancellationToken ct) =>
        Task.FromResult(Itens.FirstOrDefault(c => c.Baixas.Any(b => b.Id == baixaId)));

    public Task<IReadOnlyList<Conta>> ListarAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Conta>>(Itens.OrderBy(c => c.Vencimento).ToList());

    public Task AdicionarAsync(IReadOnlyList<Conta> contas, CancellationToken ct)
    {
        Itens.AddRange(contas);
        return Task.CompletedTask;
    }

    /// <summary>Simula outra operação mudando a conta entre a leitura e a gravação (versão da linha).</summary>
    public bool RecusarGravacao { get; set; }

    /// <summary>Simula a FK da forma de pagamento recusando a baixa nova (forma excluída depois da validação).</summary>
    public bool FormaExcluidaNaGravacao { get; set; }

    public Task<GravacaoDaConta> SalvarAsync(CancellationToken ct) => Task.FromResult(
        RecusarGravacao ? GravacaoDaConta.AlteradaAoMesmoTempo
        : FormaExcluidaNaGravacao ? GravacaoDaConta.FormaDePagamentoNaoEncontrada
        : GravacaoDaConta.Gravada);

    public Task<bool> ExisteBaixaDaFormaAsync(Guid formaDePagamentoId, CancellationToken ct) =>
        Task.FromResult(Itens.Any(c => c.Baixas.Any(b => b.FormaDePagamentoId == formaDePagamentoId)));

    /// <summary>Simula outra operação mudando a conta entre a leitura e a exclusão (versão da linha).</summary>
    public bool RecusarRemocao { get; set; }

    public Task<bool> RemoverAsync(Conta conta, CancellationToken ct)
    {
        if (RecusarRemocao)
            return Task.FromResult(false);
        Itens.Remove(conta);
        return Task.FromResult(true);
    }

    public Task<bool> ExisteDaCategoriaAsync(Guid categoriaId, CancellationToken ct) =>
        Task.FromResult(Itens.Any(c => c.CategoriaId == categoriaId));

    public Task<bool> ExisteDoFornecedorAsync(Guid fornecedorId, CancellationToken ct) =>
        Task.FromResult(Itens.Any(c => c.FornecedorId == fornecedorId));
}

internal sealed class FormasEmMemoria : IFormasDePagamentoRepository
{
    public List<FormaDePagamentoFinanceira> Itens { get; } = [];

    public Task<FormaDePagamentoFinanceira?> ObterAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(Itens.FirstOrDefault(f => f.Id == id));

    public Task<FormaDePagamentoFinanceira?> ObterPorNomeAsync(string nome, Guid? exceto, CancellationToken ct) =>
        Task.FromResult(Itens.FirstOrDefault(f =>
            string.Equals(f.Nome, nome, StringComparison.OrdinalIgnoreCase) && f.Id != exceto));

    public Task<IReadOnlyList<FormaDePagamentoFinanceira>> ListarAsync(bool incluirInativas, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<FormaDePagamentoFinanceira>>(
            Itens.Where(f => incluirInativas || f.Ativa).OrderBy(f => f.Nome).ToList());

    public Task<bool> AdicionarAsync(FormaDePagamentoFinanceira forma, CancellationToken ct)
    {
        Itens.Add(forma);
        return Task.FromResult(true);
    }

    public Task<bool> SalvarAsync(CancellationToken ct) => Task.FromResult(true);

    public Task<bool> RemoverAsync(FormaDePagamentoFinanceira forma, CancellationToken ct)
    {
        Itens.Remove(forma);
        return Task.FromResult(true);
    }
}

/// <summary>Dublê do contrato do módulo Pessoas (3.7): só os fornecedores registrados aqui existem.</summary>
internal sealed class PessoasFixas : IPessoasConsultas
{
    public List<FornecedorResumo> Fornecedores { get; } = [];

    public Task<IReadOnlyList<ClienteResumo>> BuscarClientesAsync(string busca, int limite, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ClienteResumo>>([]);

    public Task<ClienteResumo?> ObterClienteAsync(Guid clienteId, CancellationToken ct) => Task.FromResult<ClienteResumo?>(null);

    /// <summary>Quantas vezes o fornecedor foi procurado pelo documento.</summary>
    public int Consultas { get; set; }

    public Task<FornecedorResumo?> ObterFornecedorPorDocumentoAsync(string documento, CancellationToken ct)
    {
        Consultas++;
        return Task.FromResult(Fornecedores.FirstOrDefault(f => f.Documento == Impa.Shared.Documento.SomenteDigitos(documento)));
    }
}
