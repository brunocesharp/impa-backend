namespace Impa.Modules.Acesso.Contracts;

/// <summary>
/// Quem está fazendo a operação da requisição atual, para os módulos gravarem junto no registro dela (story 1-5, T4).
/// Ex.: o cancelamento de venda (story 10.2) e o desconto acima do limite (story 6.3) gravam o operador e quem liberou.
/// </summary>
public interface IContextoDeAcesso
{
    /// <summary>Usuário da sessão (o operador); <c>null</c> sem sessão.</summary>
    Guid? UsuarioId { get; }

    /// <summary>
    /// RN-1.9: quem liberou esta operação (administrador que informou usuário e senha no terminal do operador);
    /// <c>null</c> quando o operador tinha a permissão e não houve liberação.
    /// </summary>
    Guid? Liberador { get; }
}
