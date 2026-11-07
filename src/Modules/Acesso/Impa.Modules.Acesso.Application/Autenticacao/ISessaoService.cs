namespace Impa.Modules.Acesso.Application.Autenticacao;

/// <summary>Situação da conta para o login: desativada (RN-1.7) e bloqueada por tentativas (RN-1.2).</summary>
public sealed record SituacaoDaConta(bool Ativo, bool Bloqueada);

/// <summary>
/// Primitivas de identidade e sessão da requisição atual (implementadas na Infrastructure com o Identity
/// e o cookie da ADR-006). As decisões — quem entra, com que resposta — ficam nos handlers (ADR-013, regra 2).
/// </summary>
public interface ISessaoService
{
    /// <summary>Id do usuário com esse login; <c>null</c> se não existe.</summary>
    Task<Guid?> BuscarUsuarioAsync(string login, CancellationToken ct);

    /// <summary>
    /// Confere a senha do usuário, sem abrir sessão. Primitiva pura: sempre calcula o hash e não olha bloqueio
    /// nem se a conta pode entrar — essas decisões ficam no handler (<see cref="ObterSituacaoAsync"/>).
    /// Conta sem senha guardada confere contra o hash fictício e devolve <c>false</c>, com o mesmo custo.
    /// </summary>
    Task<bool> SenhaConfereAsync(Guid usuarioId, string senha, CancellationToken ct);

    /// <summary>
    /// Gasta o mesmo trabalho de uma conferência de senha, contra um hash fixo. Usado quando o login não existe,
    /// para o tempo de resposta não revelar quais logins existem.
    /// </summary>
    Task ConferirSenhaFicticiaAsync(string senha, CancellationToken ct);

    /// <summary>A conta está ativa? Está bloqueada por tentativas agora?</summary>
    Task<SituacaoDaConta> ObterSituacaoAsync(Guid usuarioId, CancellationToken ct);

    /// <summary>
    /// Conta uma senha errada seguida, de forma atômica no banco. Na 5ª, bloqueia a conta por 15 minutos e zera a
    /// contagem (RN-1.2). Devolve <c>true</c> quando a conta está bloqueada depois desta falha (bloqueou agora ou
    /// outra requisição bloqueou antes); nesse caso a falha não renova o bloqueio.
    /// </summary>
    Task<bool> RegistrarFalhaAsync(Guid usuarioId, CancellationToken ct);

    /// <summary>
    /// Login correto: zera a contagem de senhas erradas (RN-1.2), de forma atômica, só se a conta não estiver
    /// bloqueada. <c>false</c> quando outra requisição bloqueou a conta no meio do caminho.
    /// </summary>
    Task<bool> ZerarFalhasAsync(Guid usuarioId, CancellationToken ct);

    /// <summary>Abre a sessão do usuário (cookie de no máximo 12 h, RN-1.3).</summary>
    Task AbrirAsync(Guid usuarioId, CancellationToken ct);

    Task SairAsync(CancellationToken ct);

    /// <summary>Usuário da sessão atual; <c>null</c> sem sessão.</summary>
    Task<SessaoDto?> ObterAtualAsync(CancellationToken ct);
}
