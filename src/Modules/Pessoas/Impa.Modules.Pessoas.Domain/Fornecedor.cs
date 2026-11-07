using Impa.Shared;

namespace Impa.Modules.Pessoas.Domain;

/// <summary>Dados do cadastro de fornecedor (RN-14.2). Textos opcionais vazios viram nulo.</summary>
public sealed record DadosDoFornecedor(
    string RazaoSocial,
    Documento Documento,
    string? NomeFantasia,
    string? Telefone,
    string? Email,
    string? Contato);

/// <summary>
/// Fornecedor (RN-14.2): razão social e CNPJ (ou CPF, para pessoa física) obrigatórios, com documento válido e único
/// entre os fornecedores (a unicidade é do repositório/banco). Com histórico, só é inativado (RN-14.4).
/// </summary>
public sealed class Fornecedor
{
    public const int RazaoSocialMaximo = 150;
    public const int NomeFantasiaMaximo = 150;
    public const int TelefoneMaximo = 20;
    public const int EmailMaximo = 256;
    public const int ContatoMaximo = 150;

    private Fornecedor()
    {
        RazaoSocial = string.Empty;
        Documento = string.Empty;
    }

    public Guid Id { get; private set; }

    public string RazaoSocial { get; private set; }

    /// <summary>CNPJ ou CPF, só os dígitos.</summary>
    public string Documento { get; private set; }

    public string? NomeFantasia { get; private set; }

    /// <summary>Só os dígitos.</summary>
    public string? Telefone { get; private set; }

    public string? Email { get; private set; }

    public string? Contato { get; private set; }

    public bool Ativo { get; private set; }

    public DateTimeOffset CriadoEm { get; private set; }

    public static Fornecedor Criar(DadosDoFornecedor dados, DateTimeOffset agora)
    {
        var fornecedor = new Fornecedor { Id = Guid.CreateVersion7(), Ativo = true, CriadoEm = agora };
        fornecedor.Alterar(dados);
        return fornecedor;
    }

    public void Alterar(DadosDoFornecedor dados)
    {
        if (string.IsNullOrWhiteSpace(dados.RazaoSocial))
            throw new ArgumentException("A razão social do fornecedor é obrigatória", nameof(dados));
        ArgumentNullException.ThrowIfNull(dados.Documento);

        RazaoSocial = dados.RazaoSocial.Trim();
        Documento = dados.Documento.Numero;
        NomeFantasia = Textos.Opcional(dados.NomeFantasia);
        Telefone = Textos.Digitos(dados.Telefone);
        Email = Textos.Opcional(dados.Email);
        Contato = Textos.Opcional(dados.Contato);
    }

    public void Inativar() => Ativo = false;

    public void Reativar() => Ativo = true;
}
