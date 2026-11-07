namespace Impa.Modules.Acesso.Application.Usuarios;

/// <summary>Usuário na tela de gestão (story 1.4): perfil, situação e se ainda está com a senha provisória.</summary>
public sealed record UsuarioDto(
    Guid Id,
    string Usuario,
    string Nome,
    string? Email,
    Guid? PerfilId,
    string Perfil,
    bool Ativo,
    bool TrocaDeSenhaObrigatoria);

/// <summary>
/// Resposta de criar usuário e de redefinir senha: a senha provisória, mostrada uma única vez ao administrador
/// (RN-1.8). Ela não é guardada em texto em lugar nenhum; só o hash.
/// </summary>
public sealed record SenhaProvisoriaDto(Guid Id, string SenhaProvisoria)
{
    public override string ToString() => $"{nameof(SenhaProvisoriaDto)} {{ Id = {Id}, SenhaProvisoria = *** }}";
}

/// <summary>
/// Perfil na escolha da tela Usuários (<c>GET /api/usuarios/perfis</c>, decisão A1 da revisão 1): só o necessário
/// para escolher, sem as permissões nem a contagem de usuários (essas ficam em <c>/api/perfis</c>, com <c>perfis.gerenciar</c>).
/// </summary>
public sealed record PerfilResumoDto(Guid Id, string Nome, bool Protegido);

/// <summary>Dados do usuário novo, já validados e normalizados pelo handler.</summary>
public sealed record NovoUsuario(string Login, string Nome, string? Email, Guid PerfilId);

/// <summary>Cadastro de usuários (implementado na Infrastructure com o UserManager do Identity).</summary>
public interface ICadastroUsuarios
{
    /// <summary>Todos os usuários, ativos e inativos, por nome.</summary>
    Task<IReadOnlyList<UsuarioDto>> ListarAsync(CancellationToken ct);

    Task<UsuarioDto?> ObterAsync(Guid usuarioId, CancellationToken ct);

    /// <summary>Login já usado (sem diferenciar maiúsculas, como o Identity normaliza).</summary>
    Task<bool> LoginEmUsoAsync(string login, CancellationToken ct);

    /// <summary>
    /// Cria o usuário com o perfil e a senha provisória (troca obrigatória no primeiro acesso). Devolve o id, ou
    /// <c>null</c> quando o login foi tomado por outro cadastro depois da conferência (corrida): nada é gravado.
    /// </summary>
    Task<Guid?> CriarAsync(NovoUsuario dados, string senhaProvisoria, CancellationToken ct);

    /// <summary>Troca nome e e-mail (o e-mail é opcional e serve à recuperação de senha, story 1.8).</summary>
    Task AlterarDadosAsync(Guid usuarioId, string nome, string? email, CancellationToken ct);

    /// <summary>Volta a deixar o usuário entrar (o contrário da desativação, RN-1.7). Idempotente.</summary>
    Task ReativarAsync(Guid usuarioId, CancellationToken ct);

    /// <summary>
    /// Troca a senha pela provisória e marca a troca obrigatória (RN-1.8). Troca o security stamp: as sessões
    /// abertas com a senha anterior deixam de valer na próxima validação do cookie.
    /// </summary>
    Task DefinirSenhaProvisoriaAsync(Guid usuarioId, string senhaProvisoria, CancellationToken ct);
}
