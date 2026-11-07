using Impa.Modules.Pessoas.Contracts;

namespace Impa.Modules.Pessoas.Application.Comum;

/// <summary>Consulta todas as implementações de <see cref="IHistoricoDaPessoa"/> (Vendas, Compras, Financeiro).</summary>
internal static class HistoricoDasPessoas
{
    public static async Task<bool> TemAsync(
        IEnumerable<IHistoricoDaPessoa> historicos, TipoDePessoa tipo, Guid pessoaId, CancellationToken ct)
    {
        foreach (var historico in historicos)
        {
            if (await historico.TemHistoricoAsync(tipo, pessoaId, ct))
                return true;
        }

        return false;
    }
}
