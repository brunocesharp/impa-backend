namespace Impa.Modules.Financeiro.Domain;

/// <summary>Uma parcela calculada: "1/3", valor e vencimento.</summary>
public sealed record ParcelaCalculada(int Numero, int Total, decimal Valor, DateOnly Vencimento);

/// <summary>
/// RN-23.3: o valor é dividido em n parcelas iguais e a diferença de centavos fica na primeira. Os vencimentos são
/// mensais no mesmo dia do <b>primeiro</b> vencimento (não do anterior); se o mês não tiver o dia, vence no último dia
/// do mês (cenário 23.1: 31/01 → 28/02 → 31/03).
/// </summary>
public static class Parcelamento
{
    public const int ParcelasMaximo = 120;

    public static IReadOnlyList<ParcelaCalculada> Calcular(decimal valor, int parcelas, DateOnly primeiroVencimento)
    {
        Valores.ExigirValor(valor);
        if (parcelas is < 1 or > ParcelasMaximo)
            throw new ArgumentException($"Informe de 1 a {ParcelasMaximo} parcelas", nameof(parcelas));

        var centavos = (long)(valor * 100);
        if (centavos < parcelas)
            throw new ArgumentException("Cada parcela precisa ser de pelo menos R$ 0,01", nameof(parcelas));

        var demais = centavos / parcelas;
        var primeira = centavos - demais * (parcelas - 1);

        return Enumerable.Range(0, parcelas)
            .Select(i => new ParcelaCalculada(
                i + 1,
                parcelas,
                (i == 0 ? primeira : demais) / 100m,
                // DateOnly.AddMonths usa o último dia do mês quando o dia não existe nele.
                primeiroVencimento.AddMonths(i)))
            .ToList();
    }
}

internal static class Valores
{
    /// <summary>RN-23.2: valor maior que zero, em reais com até dois decimais.</summary>
    public static void ExigirValor(decimal valor)
    {
        if (valor <= 0)
            throw new ArgumentException("O valor precisa ser maior que zero", nameof(valor));
        if (valor > Conta.ValorMaximo)
            throw new ArgumentException("O valor pode ser de no máximo R$ 999.999.999.999,99", nameof(valor));
        if (decimal.Round(valor, 2) != valor)
            throw new ArgumentException("O valor tem no máximo dois decimais", nameof(valor));
    }

    public static string? Opcional(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
