namespace Impa.Modules.Configuracao.Domain.Notificacoes;

/// <summary>
/// Notificação dentro do sistema (RN-2.3): gerada a partir de um fato de outro módulo, para os usuários que tinham a
/// permissão de recebê-las no momento da criação (<see cref="Destinatarios"/>). Cada destinatário marca a própria
/// leitura (<see cref="NotificacaoLida"/>): a leitura de um não muda a do outro (cenário 2.5).
/// </summary>
public sealed class Notificacao
{
    public const int TipoMaximo = 100;
    public const int TituloMaximo = 200;
    public const int TextoMaximo = 2000;
    public const int ReferenciaMaxima = 200;

    private readonly List<NotificacaoDestinatario> _destinatarios = [];
    private readonly List<NotificacaoLida> _leituras = [];

    private Notificacao() { }

    public Guid Id { get; private set; }

    /// <summary>Tipo estável da notificação (ex.: <c>estoque.saldo-negativo</c>), usado pela tela e pelo e-mail.</summary>
    public string Tipo { get; private set; } = string.Empty;

    public string Titulo { get; private set; } = string.Empty;

    public string Texto { get; private set; } = string.Empty;

    /// <summary>Referência do dado de origem (ex.: <c>produto:{id}</c>); opcional.</summary>
    public string? ReferenciaDaOrigem { get; private set; }

    public DateTimeOffset CriadaEm { get; private set; }

    public IReadOnlyCollection<NotificacaoDestinatario> Destinatarios => _destinatarios;

    public IReadOnlyCollection<NotificacaoLida> Leituras => _leituras;

    /// <param name="destinatarios">Usuários ativos com a permissão de receber, no momento da criação. Repetidos contam uma vez.</param>
    public static Notificacao Criar(
        string tipo, string titulo, string texto, string? referenciaDaOrigem, DateTimeOffset criadaEm,
        IEnumerable<Guid> destinatarios)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tipo);
        ArgumentException.ThrowIfNullOrWhiteSpace(titulo);
        ArgumentException.ThrowIfNullOrWhiteSpace(texto);
        ArgumentNullException.ThrowIfNull(destinatarios);
        if (tipo.Length > TipoMaximo || titulo.Length > TituloMaximo || referenciaDaOrigem?.Length > ReferenciaMaxima)
            throw new ArgumentException("Tipo, título ou referência da notificação acima do tamanho máximo.");

        var notificacao = new Notificacao
        {
            Id = Guid.CreateVersion7(criadaEm),
            Tipo = tipo,
            Titulo = titulo,
            Texto = texto.Length > TextoMaximo ? texto[..TextoMaximo] : texto,
            ReferenciaDaOrigem = string.IsNullOrWhiteSpace(referenciaDaOrigem) ? null : referenciaDaOrigem,
            CriadaEm = criadaEm,
        };
        foreach (var usuarioId in destinatarios.Where(u => u != Guid.Empty).Distinct())
            notificacao._destinatarios.Add(new NotificacaoDestinatario(notificacao.Id, usuarioId));

        return notificacao;
    }

    public bool EhDestinatario(Guid usuarioId) => _destinatarios.Any(d => d.UsuarioId == usuarioId);

    public bool LidaPor(Guid usuarioId) => _leituras.Any(l => l.UsuarioId == usuarioId);

    /// <summary>
    /// Marca como lida só para <paramref name="usuarioId"/>. Já lida: nada muda (a data da primeira leitura fica).
    /// </summary>
    /// <exception cref="InvalidOperationException">O usuário não é destinatário desta notificação.</exception>
    public void MarcarComoLida(Guid usuarioId, DateTimeOffset agora)
    {
        if (!EhDestinatario(usuarioId))
            throw new InvalidOperationException("O usuário não é destinatário desta notificação.");
        if (LidaPor(usuarioId))
            return;

        _leituras.Add(new NotificacaoLida(Id, usuarioId, agora));
    }
}

/// <summary>Um destinatário da notificação: usuário que tinha a permissão de recebê-la quando ela foi criada.</summary>
public sealed class NotificacaoDestinatario
{
    private NotificacaoDestinatario() { }

    public NotificacaoDestinatario(Guid notificacaoId, Guid usuarioId)
    {
        NotificacaoId = notificacaoId;
        UsuarioId = usuarioId;
    }

    public Guid NotificacaoId { get; private set; }

    public Guid UsuarioId { get; private set; }
}

/// <summary>Leitura de uma notificação por um usuário (cada usuário marca as próprias, RN-2.3).</summary>
public sealed class NotificacaoLida
{
    private NotificacaoLida() { }

    public NotificacaoLida(Guid notificacaoId, Guid usuarioId, DateTimeOffset lidaEm)
    {
        NotificacaoId = notificacaoId;
        UsuarioId = usuarioId;
        LidaEm = lidaEm;
    }

    public Guid NotificacaoId { get; private set; }

    public Guid UsuarioId { get; private set; }

    public DateTimeOffset LidaEm { get; private set; }
}
