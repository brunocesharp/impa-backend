namespace Impa.Modules.Acesso.Application.Autenticacao;

/// <summary>Perfil do usuário e as permissões dele, lidos do banco (story 1.3).</summary>
public sealed record PermissoesCarregadas(string Perfil, IReadOnlySet<string> Permissoes)
{
    public static PermissoesCarregadas Nenhuma { get; } = new(string.Empty, new HashSet<string>());

    public bool Tem(string permissao) => Permissoes.Contains(permissao);
}

/// <summary>
/// Fonte das permissões para a autorização e o <c>/api/auth/me</c>: o perfil atual do usuário no banco, não o que
/// estava no tíquete da sessão. Cache curto em memória; quem altera perfis ou o perfil de um usuário chama
/// <see cref="Invalidar"/> e a mudança vale na próxima requisição.
/// </summary>
public interface IPermissoesDoUsuario
{
    /// <summary>Usuário inexistente ou inativo: <see cref="PermissoesCarregadas.Nenhuma"/>.</summary>
    Task<PermissoesCarregadas> ObterAsync(Guid usuarioId, CancellationToken ct);

    void Invalidar();
}
