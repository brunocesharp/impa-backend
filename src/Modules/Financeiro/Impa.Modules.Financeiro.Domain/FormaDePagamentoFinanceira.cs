namespace Impa.Modules.Financeiro.Domain;

/// <summary>
/// Forma de pagamento financeira (RN-23.4): cadastrada pelo administrador (ex.: Dinheiro, Pix, Boleto, Transferência).
/// É separada das formas de pagamento do PDV (6.5). Com pagamentos, não pode ser excluída, só inativada.
/// </summary>
public sealed class FormaDePagamentoFinanceira
{
    public const int NomeMaximo = 60;

    private FormaDePagamentoFinanceira()
    {
        Nome = string.Empty;
    }

    public Guid Id { get; private set; }

    public string Nome { get; private set; }

    public bool Ativa { get; private set; }

    public DateTimeOffset CriadaEm { get; private set; }

    public static FormaDePagamentoFinanceira Criar(string nome, DateTimeOffset agora)
    {
        var forma = new FormaDePagamentoFinanceira { Id = Guid.CreateVersion7(), Ativa = true, CriadaEm = agora };
        forma.Renomear(nome);
        return forma;
    }

    public void Renomear(string nome)
    {
        if (string.IsNullOrWhiteSpace(nome))
            throw new ArgumentException("O nome da forma de pagamento é obrigatório", nameof(nome));
        Nome = nome.Trim();
    }

    public void Inativar() => Ativa = false;

    public void Reativar() => Ativa = true;
}
