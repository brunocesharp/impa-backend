namespace Impa.Modules.Acesso.Infrastructure.Liberacoes;

/// <summary>
/// Liberação de operação por administrador (tabela <c>acesso.liberacoes</c>, RN-1.9, story 1-5). Nasce quando o
/// administrador informa a credencial no terminal do operador e é o registro da liberação: operador, quem liberou,
/// permissão e, ao ser usada, a operação (método e rota) e a data e hora. Vale uma vez, por 2 minutos, na sessão do
/// operador. O token não é gravado: só o hash.
/// </summary>
public sealed class Liberacao
{
    public const int PermissaoMaximo = 100;
    public const int OperacaoMaximo = 300;

    private Liberacao()
    {
    }

    public Liberacao(Guid id, byte[] tokenHash, Guid sessaoId, Guid operadorId, Guid liberadorId, string permissao,
        DateTimeOffset criadaEm, DateTimeOffset expiraEm)
    {
        Id = id;
        TokenHash = tokenHash;
        SessaoId = sessaoId;
        OperadorId = operadorId;
        LiberadorId = liberadorId;
        Permissao = permissao;
        CriadaEm = criadaEm;
        ExpiraEm = expiraEm;
    }

    public Guid Id { get; private set; }

    /// <summary>SHA-256 do token enviado no cabeçalho <c>X-Liberacao</c>.</summary>
    public byte[] TokenHash { get; private set; } = [];

    /// <summary>Sessão do operador (<c>acesso.sessoes</c>). Sem chave estrangeira: a limpeza apaga sessões antigas.</summary>
    public Guid SessaoId { get; private set; }

    public Guid OperadorId { get; private set; }

    /// <summary>Quem informou usuário e senha e liberou (tem "liberar operações" e a permissão).</summary>
    public Guid LiberadorId { get; private set; }

    public string Permissao { get; private set; } = string.Empty;

    public DateTimeOffset CriadaEm { get; private set; }

    public DateTimeOffset ExpiraEm { get; private set; }

    /// <summary>Quando a operação liberada foi executada; <c>null</c> enquanto não usada (ou se venceu sem uso).</summary>
    public DateTimeOffset? UsadaEm { get; private set; }

    /// <summary>Operação liberada: método e rota (ex.: <c>POST /api/vendas/12/cancelar</c>).</summary>
    public string? Operacao { get; private set; }
}
