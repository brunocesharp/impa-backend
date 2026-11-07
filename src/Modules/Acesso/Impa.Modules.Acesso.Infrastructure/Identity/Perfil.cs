using Microsoft.AspNetCore.Identity;

namespace Impa.Modules.Acesso.Infrastructure.Identity;

/// <summary>
/// Perfil de acesso (role do Identity). As permissões são claims <c>permissao</c> do perfil (tabela
/// <c>perfis_permissoes</c>); cada usuário tem um perfil (RN-1.4).
/// </summary>
public sealed class Perfil : IdentityRole<Guid>
{
    public const int NomeMaximo = 60;

    public Perfil() { }

    public Perfil(string nome, bool protegido = false) : base(nome) => Protegido = protegido;

    /// <summary>RN-1.6: perfil Administrador — não pode ser excluído nem perder permissões.</summary>
    public bool Protegido { get; private set; }

    /// <summary>Marca como protegido o Administrador criado antes da story 1.3 (seed).</summary>
    public void Proteger() => Protegido = true;
}
