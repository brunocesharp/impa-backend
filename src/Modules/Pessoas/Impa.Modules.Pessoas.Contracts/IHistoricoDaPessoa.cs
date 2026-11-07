namespace Impa.Modules.Pessoas.Contracts;

public enum TipoDePessoa
{
    Cliente,
    Fornecedor,
}

/// <summary>
/// Porta do módulo Pessoas (RN-14.4): diz se o cliente ou fornecedor tem vendas, notas ou contas vinculadas.
/// Definida aqui e implementada por quem guarda o histórico — Vendas, Compras e Financeiro (épicos 6, 11 e 12);
/// cada módulo registra a sua implementação e o Pessoas consulta todas. Com histórico, a pessoa só é inativada.
/// </summary>
public interface IHistoricoDaPessoa
{
    Task<bool> TemHistoricoAsync(TipoDePessoa tipo, Guid pessoaId, CancellationToken ct);
}
