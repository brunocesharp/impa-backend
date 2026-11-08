using Impa.Modules.Financeiro.Domain;

namespace Impa.Modules.Financeiro.Application.Categorias;

public sealed record CategoriaDto(Guid Id, string Nome, bool Ativa)
{
    public static CategoriaDto De(CategoriaFinanceira c) => new(c.Id, c.Nome, c.Ativa);
}

/// <summary>Persistência das categorias financeiras (schema <c>financeiro</c>).</summary>
public interface ICategoriasRepository
{
    Task<CategoriaFinanceira?> ObterAsync(Guid id, CancellationToken ct);

    /// <summary>Outra categoria com o mesmo nome, sem diferenciar maiúsculas; ignora <paramref name="exceto"/>.</summary>
    Task<CategoriaFinanceira?> ObterPorNomeAsync(string nome, Guid? exceto, CancellationToken ct);

    /// <summary>Em ordem de nome.</summary>
    Task<IReadOnlyList<CategoriaFinanceira>> ListarAsync(bool incluirInativas, CancellationToken ct);

    /// <summary>Falso quando o índice único do nome recusou (cadastro simultâneo).</summary>
    Task<bool> AdicionarAsync(CategoriaFinanceira categoria, CancellationToken ct);

    /// <summary>Falso quando o índice único do nome recusou.</summary>
    Task<bool> SalvarAsync(CancellationToken ct);

    /// <summary>Falso quando a chave estrangeira recusou: a categoria ganhou contas depois da consulta (RN-23.1).</summary>
    Task<bool> RemoverAsync(CategoriaFinanceira categoria, CancellationToken ct);
}
