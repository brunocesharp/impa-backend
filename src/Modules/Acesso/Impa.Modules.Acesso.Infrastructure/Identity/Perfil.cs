using Microsoft.AspNetCore.Identity;

namespace Impa.Modules.Acesso.Infrastructure.Identity;

/// <summary>Perfil de acesso (role do Identity). As permissões são claims <c>permissao</c> do perfil.</summary>
public sealed class Perfil : IdentityRole<Guid>
{
    public Perfil() { }

    public Perfil(string nome) : base(nome) { }
}
