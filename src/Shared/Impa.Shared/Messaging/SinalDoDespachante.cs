namespace Impa.Shared.Messaging;

/// <summary>
/// Sinal em memória que acorda o despachante logo após um commit que publicou eventos
/// (além da verificação periódica). Vários avisos antes do despachante acordar viram um só.
/// </summary>
public sealed class SinalDoDespachante
{
    private readonly SemaphoreSlim _sinal = new(0, 1);

    public void Acordar()
    {
        try
        {
            if (_sinal.CurrentCount == 0)
                _sinal.Release();
        }
        catch (SemaphoreFullException)
        {
            // outro aviso chegou primeiro: o despachante já vai acordar
        }
    }

    /// <summary>Espera um aviso ou o tempo indicado, o que vier primeiro.</summary>
    public async Task EsperarAsync(TimeSpan limite, CancellationToken ct) =>
        await _sinal.WaitAsync(limite, ct);
}
