using Microsoft.AspNetCore.Identity;

namespace Impa.Modules.Acesso.Infrastructure.Identity;

/// <summary>
/// Usuário do IMPA. <see cref="IdentityUser{TKey}.UserName"/> é o login; o perfil é o único role do usuário.
/// A senha fica só como hash (<see cref="IdentityUser{TKey}.PasswordHash"/>, hasher padrão do Identity).
/// </summary>
public sealed class Usuario : IdentityUser<Guid>
{
    public const int NomeExibicaoMaximo = 120;

    public string NomeExibicao { get; set; } = string.Empty;
}
