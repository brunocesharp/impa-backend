namespace Impa.Modules.Acesso.Application.Perfis;

/// <summary>Perfil com as permissões (códigos do catálogo, em ordem) e quantos usuários o têm.</summary>
public sealed record PerfilDto(Guid Id, string Nome, bool Protegido, IReadOnlyList<string> Permissoes, int Usuarios);

public static class MensagensPerfis
{
    public const string NaoEncontrado = "Perfil não encontrado";
    public const string NomeEmUso = "Já existe um perfil com esse nome";
    public const string AdministradorNaoAlteravel = "O perfil Administrador não pode ser alterado";
    public const string AdministradorNaoExcluivel = "O perfil Administrador não pode ser excluído";
    public const string PerfilComUsuarios = "O perfil tem usuários. Troque o perfil deles antes de excluir";
}

/// <summary>
/// Primitivas de perfis (implementadas na Infrastructure com o RoleManager do Identity). As regras — Administrador
/// protegido, nome único, perfil com usuários — ficam nos handlers. Quem altera invalida o cache de permissões.
/// </summary>
public interface IGestaoPerfis
{
    Task<IReadOnlyList<PerfilDto>> ListarAsync(CancellationToken ct);

    Task<PerfilDto?> ObterAsync(Guid id, CancellationToken ct);

    /// <summary>Nome já usado por outro perfil (sem diferenciar maiúsculas), exceto <paramref name="exceto"/>.</summary>
    Task<bool> NomeEmUsoAsync(string nome, Guid? exceto, CancellationToken ct);

    Task<PerfilDto> CriarAsync(string nome, IReadOnlyCollection<string> permissoes, CancellationToken ct);

    /// <summary>Troca o nome e deixa o perfil com exatamente <paramref name="permissoes"/>.</summary>
    Task<PerfilDto> AlterarAsync(Guid id, string nome, IReadOnlyCollection<string> permissoes, CancellationToken ct);

    Task ExcluirAsync(Guid id, CancellationToken ct);
}
