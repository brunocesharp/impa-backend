using Impa.Shared;

namespace Impa.Modules.Pessoas.Domain;

/// <summary>Dados do cadastro de cliente (RN-14.1). Textos opcionais vazios viram nulo.</summary>
public sealed record DadosDoCliente(
    string Nome,
    Documento? Documento,
    string? Telefone,
    string? Email,
    string? Endereco,
    string? Observacoes);

/// <summary>
/// Cliente da loja (RN-14.1): nome obrigatório; CPF ou CNPJ opcional, mas válido e único entre os clientes
/// (a unicidade é do repositório/banco). Com histórico, só é inativado (RN-14.4).
/// </summary>
public sealed class Cliente
{
    public const int NomeMaximo = 150;
    public const int TelefoneMaximo = 20;
    public const int EmailMaximo = 256;
    public const int EnderecoMaximo = 300;
    public const int ObservacoesMaximo = 1000;

    private Cliente()
    {
        Nome = string.Empty;
    }

    public Guid Id { get; private set; }

    public string Nome { get; private set; }

    /// <summary>CPF ou CNPJ, só os dígitos; nulo quando não informado.</summary>
    public string? Documento { get; private set; }

    /// <summary>Só os dígitos (a busca é por dígitos, RN-14.3).</summary>
    public string? Telefone { get; private set; }

    public string? Email { get; private set; }

    public string? Endereco { get; private set; }

    public string? Observacoes { get; private set; }

    public bool Ativo { get; private set; }

    public DateTimeOffset CriadoEm { get; private set; }

    public static Cliente Criar(DadosDoCliente dados, DateTimeOffset agora)
    {
        var cliente = new Cliente { Id = Guid.CreateVersion7(), Ativo = true, CriadoEm = agora };
        cliente.Alterar(dados);
        return cliente;
    }

    public void Alterar(DadosDoCliente dados)
    {
        if (string.IsNullOrWhiteSpace(dados.Nome))
            throw new ArgumentException("O nome do cliente é obrigatório", nameof(dados));

        Nome = dados.Nome.Trim();
        Documento = dados.Documento?.Numero;
        Telefone = Textos.Digitos(dados.Telefone);
        Email = Textos.Opcional(dados.Email);
        Endereco = Textos.Opcional(dados.Endereco);
        Observacoes = Textos.Opcional(dados.Observacoes);
    }

    public void Inativar() => Ativo = false;

    public void Reativar() => Ativo = true;
}
