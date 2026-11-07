namespace Impa.Modules.Acesso.Application.Usuarios;

/// <summary>Primitivas de gestão de usuários (implementadas na Infrastructure com o Identity).</summary>
public interface IGestaoUsuarios
{
    Task<bool> ExisteAsync(Guid usuarioId, CancellationToken ct);

    /// <summary>
    /// Marca o usuário como inativo, revoga todas as sessões abertas dele e troca o security stamp,
    /// numa transação (RN-1.7). Idempotente.
    /// </summary>
    Task DesativarAsync(Guid usuarioId, CancellationToken ct);
}
