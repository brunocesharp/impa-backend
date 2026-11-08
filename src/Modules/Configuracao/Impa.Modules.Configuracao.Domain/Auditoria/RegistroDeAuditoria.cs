namespace Impa.Modules.Configuracao.Domain.Auditoria;

/// <summary>
/// Registro de uma operação sensível (M1.6, story 4.4): quem fez, quando, em quê e os valores antes e depois.
/// Nasce do evento publicado pelo módulo de origem e não muda depois. Na v1 nada é expurgado (decisão de 07/11/2026).
/// </summary>
public sealed class RegistroDeAuditoria
{
    public const int UsuarioNomeMaximo = 200;
    public const int ModuloMaximo = 50;
    public const int EntidadeMaxima = 100;
    public const int EntidadeIdMaximo = 100;
    public const int OperacaoMaxima = 100;

    private readonly List<CampoAuditado> _campos = [];

    private RegistroDeAuditoria() { }

    /// <summary>O id do evento que originou o registro: a mesma operação nunca vira dois registros.</summary>
    public Guid Id { get; private set; }

    /// <summary>Quando a operação aconteceu (no módulo de origem).</summary>
    public DateTimeOffset OcorreuEm { get; private set; }

    /// <summary>Quando o registro foi gravado aqui.</summary>
    public DateTimeOffset RegistradoEm { get; private set; }

    public Guid UsuarioId { get; private set; }

    public string UsuarioNome { get; private set; } = string.Empty;

    public string Modulo { get; private set; } = string.Empty;

    public string Entidade { get; private set; } = string.Empty;

    public string EntidadeId { get; private set; } = string.Empty;

    public string Operacao { get; private set; } = string.Empty;

    public IReadOnlyList<CampoAuditado> Campos => _campos;

    /// <exception cref="ArgumentException">Sem usuário, módulo, entidade ou operação.</exception>
    public static RegistroDeAuditoria Criar(
        Guid id, DateTimeOffset ocorreuEm, Guid usuarioId, string usuarioNome, string modulo, string entidade,
        string entidadeId, string operacao, IEnumerable<CampoAuditado> campos, DateTimeOffset registradoEm)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Registro de auditoria sem id.", nameof(id));
        if (usuarioId == Guid.Empty)
            throw new ArgumentException("Registro de auditoria sem usuário.", nameof(usuarioId));
        ArgumentException.ThrowIfNullOrWhiteSpace(modulo);
        ArgumentException.ThrowIfNullOrWhiteSpace(entidade);
        ArgumentException.ThrowIfNullOrWhiteSpace(operacao);
        ArgumentNullException.ThrowIfNull(campos);

        var registro = new RegistroDeAuditoria
        {
            Id = id,
            OcorreuEm = ocorreuEm,
            RegistradoEm = registradoEm,
            UsuarioId = usuarioId,
            UsuarioNome = Cortar(string.IsNullOrWhiteSpace(usuarioNome) ? usuarioId.ToString() : usuarioNome.Trim(), UsuarioNomeMaximo),
            Modulo = Cortar(modulo.Trim(), ModuloMaximo),
            Entidade = Cortar(entidade.Trim(), EntidadeMaxima),
            EntidadeId = Cortar(entidadeId?.Trim() ?? string.Empty, EntidadeIdMaximo),
            Operacao = Cortar(operacao.Trim(), OperacaoMaxima),
        };
        registro._campos.AddRange(campos);
        return registro;
    }

    internal static string Cortar(string valor, int maximo) => valor.Length > maximo ? valor[..maximo] : valor;
}

/// <summary>
/// Um campo do registro. <see cref="Omitido"/>: o campo é sensível (senha, CSC, dado pessoal — CA-4); fica só o nome,
/// sem os valores.
/// </summary>
public sealed class CampoAuditado
{
    public const int CampoMaximo = 100;
    public const int ValorMaximo = 1000;

    private CampoAuditado() { }

    public string Campo { get; private set; } = string.Empty;

    public string? Antes { get; private set; }

    public string? Depois { get; private set; }

    public bool Omitido { get; private set; }

    /// <summary>
    /// Monta o campo recusando o valor quando ele é sensível. O contrato do evento já tira o valor antes do Outbox do
    /// módulo de origem; esta recusa é a segunda barreira (quem decide o que é sensível é o handler, pela marca do
    /// módulo ou pelo nome conhecido).
    /// </summary>
    public static CampoAuditado De(string campo, string? antes, string? depois, bool sensivel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(campo);
        var omitido = sensivel;

        return new CampoAuditado
        {
            Campo = RegistroDeAuditoria.Cortar(campo.Trim(), CampoMaximo),
            Antes = omitido || antes is null ? null : RegistroDeAuditoria.Cortar(antes, ValorMaximo),
            Depois = omitido || depois is null ? null : RegistroDeAuditoria.Cortar(depois, ValorMaximo),
            Omitido = omitido,
        };
    }
}
