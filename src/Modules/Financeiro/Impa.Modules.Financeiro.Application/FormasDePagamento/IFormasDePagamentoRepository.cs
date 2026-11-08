using Impa.Modules.Financeiro.Domain;

namespace Impa.Modules.Financeiro.Application.FormasDePagamento;

public sealed record FormaDePagamentoDto(Guid Id, string Nome, bool Ativa)
{
    public static FormaDePagamentoDto De(FormaDePagamentoFinanceira f) => new(f.Id, f.Nome, f.Ativa);
}

/// <summary>Persistência das formas de pagamento financeiras (schema <c>financeiro</c>, RN-23.4).</summary>
public interface IFormasDePagamentoRepository
{
    Task<FormaDePagamentoFinanceira?> ObterAsync(Guid id, CancellationToken ct);

    /// <summary>Outra forma com o mesmo nome, sem diferenciar maiúsculas; ignora <paramref name="exceto"/>.</summary>
    Task<FormaDePagamentoFinanceira?> ObterPorNomeAsync(string nome, Guid? exceto, CancellationToken ct);

    /// <summary>Em ordem de nome.</summary>
    Task<IReadOnlyList<FormaDePagamentoFinanceira>> ListarAsync(bool incluirInativas, CancellationToken ct);

    /// <summary>Falso quando o índice único do nome recusou (cadastro simultâneo).</summary>
    Task<bool> AdicionarAsync(FormaDePagamentoFinanceira forma, CancellationToken ct);

    /// <summary>Falso quando o índice único do nome recusou.</summary>
    Task<bool> SalvarAsync(CancellationToken ct);

    /// <summary>Falso quando a chave estrangeira recusou: a forma ganhou pagamentos depois da consulta.</summary>
    Task<bool> RemoverAsync(FormaDePagamentoFinanceira forma, CancellationToken ct);
}

/// <summary>Nomes das formas de pagamento para as baixas na resposta (inclui as inativas).</summary>
internal static class NomesDasFormas
{
    public static async Task<IReadOnlyDictionary<Guid, string>> ListarAsync(IFormasDePagamentoRepository formas, CancellationToken ct) =>
        (await formas.ListarAsync(incluirInativas: true, ct)).ToDictionary(f => f.Id, f => f.Nome);
}
