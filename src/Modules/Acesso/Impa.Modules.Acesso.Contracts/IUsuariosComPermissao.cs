namespace Impa.Modules.Acesso.Contracts;

/// <summary>
/// Consulta pública do Acesso para outros módulos: quem tem uma permissão agora (perfil atual no banco).
/// Ex.: destinatários das notificações (story 4.2, <see cref="Permissoes.NotificacoesReceber"/>).
/// </summary>
public interface IUsuariosComPermissao
{
    /// <summary>Ids dos usuários <b>ativos</b> cujo perfil atual tem a <paramref name="permissao"/>.</summary>
    Task<IReadOnlyList<Guid>> ListarAtivosComPermissaoAsync(string permissao, CancellationToken ct);
}
