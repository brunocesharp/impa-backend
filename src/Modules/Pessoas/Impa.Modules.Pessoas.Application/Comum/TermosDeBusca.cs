namespace Impa.Modules.Pessoas.Application.Comum;

/// <summary>
/// Busca da RN-14.3. Texto só com dígitos e máscara (".", "-", "/", "(", ")", espaço) procura pelo documento ou
/// telefone (<see cref="Digitos"/>); qualquer outro procura pelo nome, palavra por palavra, em qualquer ordem
/// (<see cref="Palavras"/>, mesmas regras da RN-3.11: sem diferenciar maiúsculas nem acentos).
/// </summary>
public sealed record TermosDeBusca(IReadOnlyList<string> Palavras, string? Digitos, bool IncluirInativos, int Limite)
{
    public const int LimitePadrao = 50;
    public const int LimiteMaximo = 200;

    private const string CaracteresDeMascara = ".-/() ";

    public bool Vazia => Palavras.Count == 0 && Digitos is null;

    public static TermosDeBusca De(string? texto, bool incluirInativos = false, int? limite = null)
    {
        var limiteFinal = Math.Clamp(limite ?? LimitePadrao, 1, LimiteMaximo);
        if (string.IsNullOrWhiteSpace(texto))
            return new TermosDeBusca([], null, incluirInativos, limiteFinal);

        var aparado = texto.Trim();
        if (aparado.Any(char.IsAsciiDigit) && aparado.All(c => char.IsAsciiDigit(c) || CaracteresDeMascara.Contains(c)))
            return new TermosDeBusca([], Impa.Shared.Documento.SomenteDigitos(aparado), incluirInativos, limiteFinal);

        var palavras = aparado.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new TermosDeBusca(palavras, null, incluirInativos, limiteFinal);
    }
}
