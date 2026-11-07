namespace Impa.Shared;

public enum TipoDeDocumento
{
    Cpf,
    Cnpj,
}

/// <summary>
/// CPF ou CNPJ válido pelos dígitos verificadores (story 3-7, RN-14.1 e RN-14.2). Aceita só dígitos ou com máscara
/// (pontos, traço, barra e espaços); guarda só os dígitos em <see cref="Numero"/>. Sequências repetidas
/// (111.111.111-11) passam na conta, mas são recusadas.
/// </summary>
public sealed record Documento
{
    public const int DigitosCpf = 11;
    public const int DigitosCnpj = 14;

    public const string MensagemCpfInvalido = "CPF inválido";
    public const string MensagemCnpjInvalido = "CNPJ inválido";
    public const string MensagemInvalido = "CPF ou CNPJ inválido";

    private Documento(TipoDeDocumento tipo, string numero)
    {
        Tipo = tipo;
        Numero = numero;
    }

    public TipoDeDocumento Tipo { get; }

    /// <summary>Só os dígitos: 11 (CPF) ou 14 (CNPJ).</summary>
    public string Numero { get; }

    /// <summary>"CPF" ou "CNPJ", para as mensagens.</summary>
    public string Sigla => Tipo == TipoDeDocumento.Cpf ? "CPF" : "CNPJ";

    /// <summary>Com máscara: 529.982.247-25 ou 11.222.333/0001-81.</summary>
    public string Formatado => Tipo == TipoDeDocumento.Cpf
        ? $"{Numero[..3]}.{Numero[3..6]}.{Numero[6..9]}-{Numero[9..]}"
        : $"{Numero[..2]}.{Numero[2..5]}.{Numero[5..8]}/{Numero[8..12]}-{Numero[12..]}";

    public static bool EstaVazio(string? texto) => string.IsNullOrWhiteSpace(texto);

    /// <summary>Os dígitos do texto, sem nenhum outro caractere (vazio para nulo).</summary>
    public static string SomenteDigitos(string? texto) =>
        texto is null ? string.Empty : new string(texto.Where(char.IsAsciiDigit).ToArray());

    /// <summary>
    /// Lê o documento. Falha com "CPF inválido" (11 dígitos), "CNPJ inválido" (14 dígitos) ou
    /// <see cref="MensagemInvalido"/> (outra quantidade de dígitos ou caractere fora da máscara). Vazio também falha,
    /// sem mensagem: quem chama decide se o documento é opcional (<see cref="EstaVazio"/>).
    /// </summary>
    public static bool TentarCriar(string? texto, out Documento? documento, out string? erro)
    {
        documento = null;
        erro = null;
        if (EstaVazio(texto))
            return false;

        if (!texto!.All(c => char.IsAsciiDigit(c) || c is '.' or '-' or '/' or ' '))
        {
            erro = MensagemInvalido;
            return false;
        }

        var digitos = SomenteDigitos(texto);
        switch (digitos.Length)
        {
            case DigitosCpf when CpfValido(digitos):
                documento = new Documento(TipoDeDocumento.Cpf, digitos);
                return true;
            case DigitosCpf:
                erro = MensagemCpfInvalido;
                return false;
            case DigitosCnpj when CnpjValido(digitos):
                documento = new Documento(TipoDeDocumento.Cnpj, digitos);
                return true;
            case DigitosCnpj:
                erro = MensagemCnpjInvalido;
                return false;
            default:
                erro = MensagemInvalido;
                return false;
        }
    }

    /// <summary>Documento já validado (ex.: lido do banco). Inválido: <see cref="ArgumentException"/>.</summary>
    public static Documento Criar(string texto) =>
        TentarCriar(texto, out var documento, out var erro)
            ? documento!
            : throw new ArgumentException(erro ?? MensagemInvalido, nameof(texto));

    /// <summary>Validação para regras (ex.: FluentValidation): nulo quando válido.</summary>
    public static string? Erro(string? texto) =>
        TentarCriar(texto, out _, out var erro) ? null : erro ?? MensagemInvalido;

    public override string ToString() => Formatado;

    private static bool TodosIguais(string digitos) => digitos.All(c => c == digitos[0]);

    private static bool CpfValido(string d)
    {
        if (TodosIguais(d))
            return false;

        return d[9] - '0' == DigitoCpf(d, 9) && d[10] - '0' == DigitoCpf(d, 10);
    }

    private static int DigitoCpf(string d, int quantidade)
    {
        var soma = 0;
        for (var i = 0; i < quantidade; i++)
            soma += (d[i] - '0') * (quantidade + 1 - i);
        var resto = soma % 11;
        return resto < 2 ? 0 : 11 - resto;
    }

    private static readonly int[] PesosCnpj1 = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
    private static readonly int[] PesosCnpj2 = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

    private static bool CnpjValido(string d)
    {
        if (TodosIguais(d))
            return false;

        return d[12] - '0' == DigitoCnpj(d, PesosCnpj1) && d[13] - '0' == DigitoCnpj(d, PesosCnpj2);
    }

    private static int DigitoCnpj(string d, int[] pesos)
    {
        var soma = 0;
        for (var i = 0; i < pesos.Length; i++)
            soma += (d[i] - '0') * pesos[i];
        var resto = soma % 11;
        return resto < 2 ? 0 : 11 - resto;
    }
}
