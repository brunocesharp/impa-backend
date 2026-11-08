using Impa.Modules.Financeiro.Application.Contas;
using Impa.Modules.Pessoas.Contracts;

namespace Impa.Modules.Financeiro.Application;

/// <summary>
/// Implementação do Financeiro da porta <see cref="IHistoricoDaPessoa"/> do módulo Pessoas (RN-14.4): fornecedor com
/// conta vinculada tem histórico e só pode ser inativado. Contas não têm cliente, então o cliente nunca tem histórico aqui.
/// </summary>
public sealed class HistoricoDaPessoaNoFinanceiro(IContasRepository contas) : IHistoricoDaPessoa
{
    public Task<bool> TemHistoricoAsync(TipoDePessoa tipo, Guid pessoaId, CancellationToken ct) =>
        tipo == TipoDePessoa.Fornecedor ? contas.ExisteDoFornecedorAsync(pessoaId, ct) : Task.FromResult(false);
}
