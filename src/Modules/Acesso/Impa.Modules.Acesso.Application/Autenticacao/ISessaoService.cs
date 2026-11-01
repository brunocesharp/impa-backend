namespace Impa.Modules.Acesso.Application.Autenticacao;

/// <summary>
/// Primitivas de identidade e sessão da requisição atual (implementadas na Infrastructure com o Identity
/// e o cookie da ADR-006). As decisões — quem entra, com que resposta — ficam nos handlers (ADR-013, regra 2).
/// </summary>
public interface ISessaoService
{
    /// <summary>Id do usuário com esse login; <c>null</c> se não existe.</summary>
    Task<Guid?> BuscarUsuarioAsync(string login, CancellationToken ct);

    /// <summary>Confere a senha do usuário (sem abrir sessão).</summary>
    Task<bool> SenhaConfereAsync(Guid usuarioId, string senha, CancellationToken ct);

    /// <summary>
    /// Gasta o mesmo trabalho de uma conferência de senha, contra um hash fixo. Usado quando o login não existe,
    /// para o tempo de resposta não revelar quais logins existem.
    /// </summary>
    Task ConferirSenhaFicticiaAsync(string senha, CancellationToken ct);

    /// <summary>Abre a sessão do usuário (cookie de no máximo 12 h, RN-1.3).</summary>
    Task AbrirAsync(Guid usuarioId, CancellationToken ct);

    Task SairAsync(CancellationToken ct);

    /// <summary>Usuário da sessão atual; <c>null</c> sem sessão.</summary>
    Task<SessaoDto?> ObterAtualAsync(CancellationToken ct);
}
