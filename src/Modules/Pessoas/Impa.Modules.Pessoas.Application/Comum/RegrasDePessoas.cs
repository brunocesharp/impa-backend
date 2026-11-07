using FluentValidation;
using Impa.Shared;

namespace Impa.Modules.Pessoas.Application.Comum;

/// <summary>Mensagens do cadastro de pessoas. As de documento repetido são as do cenário 14.2 e da decisão de 07/11/2026.</summary>
public static class MensagensPessoas
{
    public const string ClienteNaoEncontrado = "Cliente não encontrado";
    public const string FornecedorNaoEncontrado = "Fornecedor não encontrado";

    /// <summary>Corrida rara: o índice único recusou, mas o outro cadastro ainda não é visível.</summary>
    public const string DocumentoJaCadastrado = "Documento já cadastrado";

    /// <summary>Cenário 14.2.</summary>
    public const string DocumentoDoFornecedorObrigatorio = "Informe o CNPJ ou CPF do fornecedor";

    /// <summary>Cenário 14.3 (RN-14.4).</summary>
    public const string ClienteComHistorico = "Este cliente tem histórico. Você pode inativá-lo";

    /// <summary>O equivalente do cenário 14.3 para fornecedor (RN-14.4).</summary>
    public const string FornecedorComHistorico = "Este fornecedor tem histórico. Você pode inativá-lo";

    /// <summary>"CPF já cadastrado para Maria Souza" (cenário 14.2) ou "CNPJ já cadastrado para {nome}".</summary>
    public static string DocumentoDeClienteRepetido(Documento documento, string nome) =>
        $"{documento.Sigla} já cadastrado para {nome}";

    /// <summary>"CNPJ já cadastrado para o fornecedor {razão social}" (e o análogo para CPF).</summary>
    public static string DocumentoDeFornecedorRepetido(Documento documento, string razaoSocial) =>
        $"{documento.Sigla} já cadastrado para o fornecedor {razaoSocial}";
}

/// <summary>Validações comuns do cadastro de clientes e fornecedores (RN-14.1, RN-14.2).</summary>
public static class RegrasDePessoas
{
    private const string CaracteresDoTelefone = "0123456789()+-. ";
    private const int DigitosDoTelefoneMaximo = 15;

    public static void Obrigatorio<T>(IRuleBuilder<T, string> regra, string mensagem, int maximo) => regra
        .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage(mensagem)
        .Must(n => n is null || n.Trim().Length <= maximo).WithMessage($"Use no máximo {maximo} caracteres");

    public static void Opcional<T>(IRuleBuilder<T, string?> regra, int maximo) => regra
        .Must(t => string.IsNullOrWhiteSpace(t) || t.Trim().Length <= maximo)
        .WithMessage($"Use no máximo {maximo} caracteres");

    /// <summary>Cliente: opcional, mas válido quando informado (RN-14.1).</summary>
    public static void DocumentoOpcional<T>(IRuleBuilder<T, string?> regra) => regra
        .Must(d => Documento.EstaVazio(d) || Documento.Erro(d) is null)
        .WithMessage((_, d) => Documento.Erro(d));

    /// <summary>Fornecedor: obrigatório e válido (RN-14.2).</summary>
    public static void DocumentoObrigatorio<T>(IRuleBuilder<T, string?> regra) => regra
        .Must(d => !Documento.EstaVazio(d)).WithMessage(MensagensPessoas.DocumentoDoFornecedorObrigatorio)
        .Must(d => Documento.EstaVazio(d) || Documento.Erro(d) is null)
        .WithMessage((_, d) => Documento.Erro(d));

    public static void Telefone<T>(IRuleBuilder<T, string?> regra) => regra
        .Must(t => string.IsNullOrWhiteSpace(t)
                   || (t.All(CaracteresDoTelefone.Contains)
                       && Documento.SomenteDigitos(t).Length is > 0 and <= DigitosDoTelefoneMaximo))
        .WithMessage("Telefone inválido");

    public static void Email<T>(IRuleBuilder<T, string?> regra, int maximo) => regra
        .Must(e => string.IsNullOrWhiteSpace(e) || e.Trim().Length <= maximo)
        .WithMessage($"O e-mail tem no máximo {maximo} caracteres")
        .Must(e => string.IsNullOrWhiteSpace(e) || EmailValido(e.Trim())).WithMessage("E-mail inválido");

    /// <summary>Documento já validado pelo validator; vazio vira nulo.</summary>
    public static Documento? DocumentoOuNulo(string? texto) =>
        Documento.EstaVazio(texto) ? null : Documento.Criar(texto!);

    private static bool EmailValido(string email)
    {
        var arroba = email.IndexOf('@');
        return arroba > 0 && arroba == email.LastIndexOf('@') && arroba < email.Length - 1
               && !email.Any(char.IsWhiteSpace) && email[(arroba + 1)..].Contains('.');
    }
}
