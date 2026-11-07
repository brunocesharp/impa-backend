namespace Impa.Modules.Acesso.Infrastructure.Terminais;

/// <summary>
/// Computador ou celular registrado pelo administrador para usar o PDV e o caixa (tabela <c>acesso.terminais</c>,
/// RN-11.1, ADR-006/ADR-010). O dispositivo guarda o token no cookie <c>impa_terminal</c>; aqui fica só o hash.
/// </summary>
public sealed class Terminal
{
    public const int NomeMaximo = 60;

    private Terminal()
    {
    }

    public Terminal(Guid id, string nome, byte[] tokenHash, Guid criadoPor, DateTimeOffset criadoEm)
    {
        Id = id;
        Renomear(nome);
        TokenHash = tokenHash;
        CriadoPor = criadoPor;
        CriadoEm = criadoEm;
    }

    public Guid Id { get; private set; }

    /// <summary>Nome da tela (ex.: "Balcão 1", "Celular Loja"). Único entre os terminais não revogados.</summary>
    public string Nome { get; private set; } = string.Empty;

    /// <summary>Nome em maiúsculas, para a unicidade sem diferenciar maiúsculas (índice parcial só dos ativos).</summary>
    public string NomeNormalizado { get; private set; } = string.Empty;

    /// <summary>SHA-256 do token do dispositivo (o token em si nunca é gravado).</summary>
    public byte[] TokenHash { get; private set; } = [];

    /// <summary>Usuário (administrador) que registrou o terminal.</summary>
    public Guid CriadoPor { get; private set; }

    public DateTimeOffset CriadoEm { get; private set; }

    /// <summary>Revogado não volta a valer: o dispositivo precisa ser registrado de novo.</summary>
    public DateTimeOffset? RevogadoEm { get; private set; }

    public static string Normalizar(string nome) => nome.ToUpperInvariant();

    public void Renomear(string nome)
    {
        Nome = nome;
        NomeNormalizado = Normalizar(nome);
    }

    public void Revogar(DateTimeOffset agora) => RevogadoEm ??= agora;
}
