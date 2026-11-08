using System.Globalization;
using System.Text;

namespace Impa.Modules.Configuracao.Contracts.Auditoria;

/// <summary>
/// Nomes de campo cujo valor nunca sai do módulo de origem nem é gravado na auditoria (CA-4): senha e CSC (RN-15.6) e os dados pessoais da
/// RN-14.6 (CPF, telefone, e-mail, endereço). Compara por palavra do nome (<c>senhaDoCertificado</c>,
/// <c>cpf_do_cliente</c>, <c>Endereco.Logradouro</c>), sem acento e sem diferenciar maiúsculas.
/// </summary>
public static class CamposSensiveis
{
    private static readonly HashSet<string> Palavras = new(StringComparer.Ordinal)
    {
        "senha", "password", "csc", "token", "segredo", "secret",
        "cpf", "telefone", "fone", "celular", "whatsapp", "email", "endereco", "logradouro", "cep", "bairro",
    };

    public static bool EhSensivel(string campo)
    {
        if (string.IsNullOrWhiteSpace(campo))
            return false;

        var semAcento = SemAcento(campo);
        var juntas = new string(semAcento.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return Palavras.Contains(juntas) || PalavrasDe(semAcento).Any(Palavras.Contains);
    }

    /// <summary>Quebra por separador (<c>_ . - espaço</c>) e por maiúscula (camelCase/PascalCase).</summary>
    private static IEnumerable<string> PalavrasDe(string nome)
    {
        var atual = new StringBuilder();
        for (var i = 0; i < nome.Length; i++)
        {
            var c = nome[i];
            if (!char.IsLetterOrDigit(c))
            {
                if (atual.Length > 0) yield return atual.ToString().ToLowerInvariant();
                atual.Clear();
                continue;
            }

            var inicioDePalavra = char.IsUpper(c) && atual.Length > 0
                && (char.IsLower(nome[i - 1]) || (i + 1 < nome.Length && char.IsLower(nome[i + 1])));
            if (inicioDePalavra)
            {
                yield return atual.ToString().ToLowerInvariant();
                atual.Clear();
            }

            atual.Append(c);
        }

        if (atual.Length > 0)
            yield return atual.ToString().ToLowerInvariant();
    }

    private static string SemAcento(string texto)
    {
        var decomposto = texto.Normalize(NormalizationForm.FormD);
        var resultado = new StringBuilder(decomposto.Length);
        foreach (var c in decomposto)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                resultado.Append(c);
        return resultado.ToString().Normalize(NormalizationForm.FormC);
    }
}
