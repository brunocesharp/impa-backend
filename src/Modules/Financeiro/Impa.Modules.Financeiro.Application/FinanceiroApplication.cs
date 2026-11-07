using System.Reflection;

namespace Impa.Modules.Financeiro.Application;

/// <summary>Marcador do assembly: o Host registra daqui os handlers (MediatR) e os validators (FluentValidation).</summary>
public static class FinanceiroApplication
{
    public static Assembly Assembly { get; } = typeof(FinanceiroApplication).Assembly;
}

/// <summary>Mensagens do módulo Financeiro (story 11-1).</summary>
public static class MensagensFinanceiro
{
    public const string CategoriaNaoEncontrada = "Categoria não encontrada";
    public const string CategoriaInativa = "Categoria inativa";
    public const string ContaNaoEncontrada = "Conta não encontrada";
    public const string FornecedorNaoEncontrado = "Fornecedor não encontrado";

    /// <summary>RN-23.1, no mesmo tom do cenário 14.3.</summary>
    public const string CategoriaComContas = "Esta categoria tem contas. Você pode inativá-la";

    public static string CategoriaRepetida(string nome) => $"Já existe a categoria {nome}";
}
