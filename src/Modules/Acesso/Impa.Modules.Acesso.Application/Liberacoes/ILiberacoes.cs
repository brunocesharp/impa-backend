namespace Impa.Modules.Acesso.Application.Liberacoes;

/// <summary>
/// Liberação concedida por um administrador (RN-1.9) e ainda não usada: vale para uma operação da
/// <paramref name="SessaoId"/> do operador que exige <paramref name="Permissao"/>, até <paramref name="ExpiraEm"/>.
/// O token nunca chega aqui: só o hash (<see cref="TokenDeLiberacao.Hash"/>).
/// </summary>
public sealed record NovaLiberacao(
    byte[] TokenHash,
    Guid SessaoId,
    Guid OperadorId,
    Guid LiberadorId,
    string Permissao,
    DateTimeOffset ExpiraEm);

public static class MensagensLiberacao
{
    public const string PermissaoNaoLiberavel = "Esta operação não pode ser liberada";

    /// <summary>Credencial certa, mas o usuário não tem "liberar operações" ou a permissão pedida (RN-1.9).</summary>
    public const string SemPermissaoParaLiberar = "Este usuário não pode liberar esta operação";
}

/// <summary>
/// Primitivas das liberações (tabela <c>acesso.liberacoes</c>, implementadas na Infrastructure). As regras — quem pode
/// liberar, credencial e bloqueio — ficam no handler; o consumo é atômico no banco (uso único).
/// </summary>
public interface ILiberacoes
{
    Task RegistrarAsync(NovaLiberacao liberacao, CancellationToken ct);

    /// <summary>
    /// Quem liberou, se a liberação ainda vale (mesmas condições de <see cref="ConsumirAsync"/>), sem consumi-la — para
    /// conferir que o liberador ainda pode liberar antes de gastar o token.
    /// </summary>
    Task<Guid?> ObterLiberadorAsync(byte[] tokenHash, Guid sessaoId, string permissao, CancellationToken ct);

    /// <summary>
    /// Consome a liberação, se ainda vale: mesmo hash, mesma sessão, mesma permissão, não usada e não vencida. Marca o uso
    /// e a <paramref name="operacao"/> (método e rota) de forma atômica — duas requisições com o mesmo token não passam
    /// as duas. Devolve quem liberou; <c>null</c> se a liberação não vale.
    /// </summary>
    Task<Guid?> ConsumirAsync(byte[] tokenHash, Guid sessaoId, string permissao, string operacao, CancellationToken ct);
}
