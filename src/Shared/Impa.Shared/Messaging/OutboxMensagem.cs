namespace Impa.Shared.Messaging;

public enum SituacaoDoEvento
{
    /// <summary>Aguardando entrega (ou nova tentativa após falha).</summary>
    Pendente,

    /// <summary>Entregue a todos os consumidores.</summary>
    Entregue,

    /// <summary>Passou do limite de tentativas; espera reprocessamento manual.</summary>
    ComFalha,
}

/// <summary>
/// Linha do Outbox de um módulo: o evento gravado na mesma transação dos dados (ADR-011)
/// e o estado da entrega feita pelo despachante.
/// </summary>
public sealed class OutboxMensagem
{
    private OutboxMensagem() { }

    /// <summary>Igual ao <see cref="IIntegrationEvent.Id"/>.</summary>
    public Guid Id { get; private set; }

    public string Tipo { get; private set; } = string.Empty;

    public int Versao { get; private set; }

    public string? ChaveDeCorrelacao { get; private set; }

    /// <summary>Evento serializado (JSON).</summary>
    public string Conteudo { get; private set; } = string.Empty;

    public DateTimeOffset OcorreuEm { get; private set; }

    public SituacaoDoEvento Situacao { get; private set; }

    public int Tentativas { get; private set; }

    /// <summary>A partir de quando o despachante pode (re)tentar a entrega.</summary>
    public DateTimeOffset ProximaTentativaEm { get; private set; }

    public string? UltimoErro { get; private set; }

    public DateTimeOffset? EntregueEm { get; private set; }

    public static OutboxMensagem Nova(IIntegrationEvent evento, string conteudo, DateTimeOffset agora) => new()
    {
        Id = evento.Id,
        Tipo = evento.Tipo,
        Versao = evento.Versao,
        ChaveDeCorrelacao = evento.ChaveDeCorrelacao,
        Conteudo = conteudo,
        OcorreuEm = evento.OcorreuEm,
        Situacao = SituacaoDoEvento.Pendente,
        Tentativas = 0,
        ProximaTentativaEm = agora,
    };

    public void MarcarEntregue(DateTimeOffset agora)
    {
        Situacao = SituacaoDoEvento.Entregue;
        EntregueEm = agora;
    }

    /// <summary>
    /// Conta a tentativa que falhou. Abaixo do <paramref name="limite"/>, agenda a próxima para
    /// <paramref name="agora"/> + <paramref name="intervalo"/>; no limite, fica <see cref="SituacaoDoEvento.ComFalha"/>.
    /// </summary>
    /// <returns><c>true</c> se o evento ficou com falha.</returns>
    public bool RegistrarFalha(string erro, DateTimeOffset agora, TimeSpan intervalo, int limite)
    {
        Tentativas++;
        UltimoErro = erro;
        if (Tentativas >= limite)
        {
            Situacao = SituacaoDoEvento.ComFalha;
            return true;
        }

        ProximaTentativaEm = agora + intervalo;
        return false;
    }
}

/// <summary>
/// Linha do Inbox de um módulo consumidor: o evento já processado por um handler, gravada na mesma
/// transação do efeito. Evento + handler repetidos são ignorados (efeito exatamente uma vez).
/// </summary>
public sealed class InboxMensagem
{
    private InboxMensagem() { }

    public Guid EventoId { get; private set; }

    /// <summary>Nome estável do handler (nome completo da classe).</summary>
    public string Handler { get; private set; } = string.Empty;

    public DateTimeOffset ProcessadoEm { get; private set; }

    public static InboxMensagem Nova(Guid eventoId, string handler, DateTimeOffset agora) => new()
    {
        EventoId = eventoId,
        Handler = handler,
        ProcessadoEm = agora,
    };
}
