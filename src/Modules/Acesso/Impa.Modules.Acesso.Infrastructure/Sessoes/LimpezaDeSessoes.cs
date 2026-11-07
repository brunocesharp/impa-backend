using Impa.Modules.Acesso.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Impa.Modules.Acesso.Infrastructure.Sessoes;

/// <summary>Apaga as sessões terminadas há mais tempo que a retenção (story 1-11). Chamada pela tarefa do Host.</summary>
public interface ILimpezaDeSessoes
{
    /// <summary>Apaga as sessões revogadas ou vencidas há mais de <paramref name="retencao"/>; devolve quantas apagou.</summary>
    Task<int> ApagarAntigasAsync(TimeSpan retencao, CancellationToken ct);
}

/// <summary>
/// Uma sessão terminou quando foi revogada (logout, desativação) ou venceu (12 h depois do login).
/// Terminada há mais que a retenção, é apagada; sessão válida (não revogada e com fim no futuro)
/// nunca é apagada. Mexe só em <c>acesso.sessoes</c> (ADR-012).
/// </summary>
public sealed class LimpezaDeSessoes(AcessoDbContext banco, TimeProvider relogio) : ILimpezaDeSessoes
{
    public Task<int> ApagarAntigasAsync(TimeSpan retencao, CancellationToken ct)
    {
        var limite = relogio.GetUtcNow() - retencao;
        return banco.Sessoes
            .Where(s => (s.RevogadaEm != null && s.RevogadaEm < limite) || s.ExpiraEm < limite)
            .ExecuteDeleteAsync(ct);
    }
}
