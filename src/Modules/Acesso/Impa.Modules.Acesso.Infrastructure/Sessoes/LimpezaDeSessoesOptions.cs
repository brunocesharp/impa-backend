namespace Impa.Modules.Acesso.Infrastructure.Sessoes;

/// <summary>Limpeza das sessões antigas (seção <c>Acesso:LimpezaDeSessoes</c>, story 1-11).</summary>
public sealed class LimpezaDeSessoesOptions
{
    public const string Secao = "Acesso:LimpezaDeSessoes";

    /// <summary>Dias que uma sessão revogada ou vencida fica, como registro de acesso, antes de ser apagada.</summary>
    public int RetencaoEmDias { get; set; } = 30;

    /// <summary>Horas entre as execuções da limpeza (a primeira roda logo que a API sobe).</summary>
    public int IntervaloEmHoras { get; set; } = 24;

    public TimeSpan Retencao => TimeSpan.FromDays(RetencaoEmDias);

    public TimeSpan Intervalo => TimeSpan.FromHours(IntervaloEmHoras);
}
