namespace Impa.Modules.Acesso.Application.Autenticacao;

/// <summary>
/// Sessão do usuário na requisição atual (implementada na Infrastructure com o Identity e o cookie da ADR-006).
/// </summary>
public interface ISessaoService
{
    /// <summary>Confere a credencial e, se válida, abre a sessão. Não diz qual dado está errado.</summary>
    Task<bool> EntrarAsync(string usuario, string senha, CancellationToken ct);

    Task SairAsync(CancellationToken ct);

    /// <summary>Usuário da sessão atual; <c>null</c> sem sessão.</summary>
    Task<SessaoDto?> ObterAtualAsync(CancellationToken ct);
}
