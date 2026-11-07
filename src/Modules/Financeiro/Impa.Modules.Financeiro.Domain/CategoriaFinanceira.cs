namespace Impa.Modules.Financeiro.Domain;

/// <summary>
/// Categoria financeira (RN-23.1): cadastrada pelo administrador (ex.: Compra de mercadorias, Aluguel, Energia,
/// Internet, Impostos). Com contas, não pode ser excluída, só inativada.
/// </summary>
public sealed class CategoriaFinanceira
{
    public const int NomeMaximo = 100;

    private CategoriaFinanceira()
    {
        Nome = string.Empty;
    }

    public Guid Id { get; private set; }

    public string Nome { get; private set; }

    public bool Ativa { get; private set; }

    public DateTimeOffset CriadaEm { get; private set; }

    public static CategoriaFinanceira Criar(string nome, DateTimeOffset agora)
    {
        var categoria = new CategoriaFinanceira { Id = Guid.CreateVersion7(), Ativa = true, CriadaEm = agora };
        categoria.Renomear(nome);
        return categoria;
    }

    public void Renomear(string nome)
    {
        if (string.IsNullOrWhiteSpace(nome))
            throw new ArgumentException("O nome da categoria é obrigatório", nameof(nome));
        Nome = nome.Trim();
    }

    public void Inativar() => Ativa = false;

    public void Reativar() => Ativa = true;
}
