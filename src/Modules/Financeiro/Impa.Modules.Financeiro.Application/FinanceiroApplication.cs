using System.Reflection;

namespace Impa.Modules.Financeiro.Application;

/// <summary>Marcador do assembly: o Host registra daqui os handlers (MediatR) e os validators (FluentValidation).</summary>
public static class FinanceiroApplication
{
    public static Assembly Assembly { get; } = typeof(FinanceiroApplication).Assembly;
}

/// <summary>Mensagens do módulo Financeiro (stories 11-1 e 11-2).</summary>
public static class MensagensFinanceiro
{
    public const string CategoriaNaoEncontrada = "Categoria não encontrada";
    public const string CategoriaInativa = "Categoria inativa";
    public const string ContaNaoEncontrada = "Conta não encontrada";
    public const string FornecedorNaoEncontrado = "Fornecedor não encontrado";

    /// <summary>RN-23.1, no mesmo tom do cenário 14.3.</summary>
    public const string CategoriaComContas = "Esta categoria tem contas. Você pode inativá-la";

    public static string CategoriaRepetida(string nome) => $"Já existe a categoria {nome}";

    public const string FormaDePagamentoNaoEncontrada = "Forma de pagamento não encontrada";
    public const string FormaDePagamentoInativa = "Forma de pagamento inativa";
    public const string FormaDePagamentoComPagamentos = "Esta forma de pagamento tem pagamentos. Você pode inativá-la";

    public static string FormaDePagamentoRepetida(string nome) => $"Já existe a forma de pagamento {nome}";

    public const string PagamentoNaoEncontrado = "Pagamento não encontrado";
    public const string PagamentoJaEstornado = "Este pagamento já foi estornado";

    /// <summary>RN-23.7: com pagamento, só descrição e observação são editáveis.</summary>
    public const string CampoTravadoPeloPagamento = "Conta com pagamento: só a descrição e a observação podem ser alteradas";

    /// <summary>RN-23.7: conta com pagamento não pode ser excluída.</summary>
    public const string ContaComPagamento = "Conta com pagamento não pode ser excluída. Estorne os pagamentos antes";

    /// <summary>Outra operação mudou a conta entre a leitura e a gravação (versão da linha).</summary>
    public const string ContaAlteradaAoMesmoTempo = "A conta foi alterada em outra operação. Atualize e tente de novo";
}
