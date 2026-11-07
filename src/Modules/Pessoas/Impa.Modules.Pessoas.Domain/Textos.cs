namespace Impa.Modules.Pessoas.Domain;

internal static class Textos
{
    public static string? Opcional(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    /// <summary>Só os dígitos; nulo quando não sobra nenhum.</summary>
    public static string? Digitos(string? texto)
    {
        var digitos = Impa.Shared.Documento.SomenteDigitos(texto);
        return digitos.Length == 0 ? null : digitos;
    }
}
