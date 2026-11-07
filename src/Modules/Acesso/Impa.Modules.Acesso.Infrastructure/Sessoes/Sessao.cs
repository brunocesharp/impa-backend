namespace Impa.Modules.Acesso.Infrastructure.Sessoes;

/// <summary>
/// Sessão aberta no servidor (tabela <c>acesso.sessoes</c>, ADR-006). O cookie leva só o <see cref="Id"/>;
/// o tíquete de autenticação fica aqui, e revogar a sessão vale na próxima requisição (RN-1.7).
/// </summary>
public sealed class Sessao
{
    private Sessao()
    {
    }

    public Sessao(Guid id, Guid usuarioId, DateTimeOffset criadaEm, DateTimeOffset expiraEm, byte[] tiquete)
    {
        Id = id;
        UsuarioId = usuarioId;
        CriadaEm = criadaEm;
        ExpiraEm = expiraEm;
        Tiquete = tiquete;
    }

    public Guid Id { get; private set; }

    public Guid UsuarioId { get; private set; }

    public DateTimeOffset CriadaEm { get; private set; }

    /// <summary>Fim da sessão: 12 h depois do login, sem expiração deslizante (RN-1.3).</summary>
    public DateTimeOffset ExpiraEm { get; private set; }

    /// <summary>Logout ou desativação do usuário. Revogada não volta a valer.</summary>
    public DateTimeOffset? RevogadaEm { get; private set; }

    /// <summary>Tíquete serializado (<c>TicketSerializer</c>): claims do usuário e propriedades do cookie.</summary>
    public byte[] Tiquete { get; private set; } = [];

    public bool ValeEm(DateTimeOffset agora) => RevogadaEm is null && agora < ExpiraEm;

    public void Renovar(byte[] tiquete, DateTimeOffset? expiraEm)
    {
        Tiquete = tiquete;
        if (expiraEm is not null)
            ExpiraEm = expiraEm.Value;
    }

    public void Revogar(DateTimeOffset agora) => RevogadaEm ??= agora;
}
