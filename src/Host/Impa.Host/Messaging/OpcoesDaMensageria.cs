namespace Impa.Host.Messaging;

/// <summary>Configuração do despachante (seção <c>Mensageria</c>).</summary>
public sealed class OpcoesDaMensageria
{
    public const string Secao = "Mensageria";

    /// <summary>Intervalos padrão entre as tentativas (ADR-011: intervalo crescente).</summary>
    public static readonly TimeSpan[] IntervalosPadrao =
    [
        TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromHours(1),
        TimeSpan.FromHours(2),
    ];

    /// <summary>Verificação periódica dos Outbox, além do aviso após cada commit.</summary>
    public TimeSpan IntervaloDeVerificacao { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Eventos lidos por vez de cada Outbox.</summary>
    public int TamanhoDoLote { get; set; } = 50;

    /// <summary>Tentativas até o evento ficar "com falha".</summary>
    public int LimiteDeTentativas { get; set; } = 10;

    /// <summary>Sobrescreve <see cref="IntervalosPadrao"/> quando configurado; vazio, vale o padrão.</summary>
    public TimeSpan[] IntervalosDeNovaTentativa { get; set; } = [];

    /// <summary>Espera antes da próxima tentativa, depois de <paramref name="tentativas"/> falhas (1 = primeira).</summary>
    public TimeSpan IntervaloAposTentativa(int tentativas)
    {
        var intervalos = IntervalosDeNovaTentativa.Length > 0 ? IntervalosDeNovaTentativa : IntervalosPadrao;
        return intervalos[Math.Clamp(tentativas - 1, 0, intervalos.Length - 1)];
    }
}
