using Impa.Modules.Financeiro.Application;
using Impa.Modules.Financeiro.Application.Categorias;
using Impa.Modules.Financeiro.Application.Contas;
using Impa.Modules.Financeiro.Application.FormasDePagamento;
using Impa.Modules.Financeiro.Infrastructure.Persistence;
using Impa.Modules.Pessoas.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Impa.Modules.Financeiro.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registra o módulo Financeiro (contas a pagar, stories 11-1 e 11-2): banco no schema <c>financeiro</c>, repositórios e a
    /// implementação da porta <see cref="IHistoricoDaPessoa"/> do Pessoas (fornecedor com conta não é excluído).
    /// O fornecedor da conta vem do contrato <see cref="IPessoasConsultas"/>, registrado pelo módulo Pessoas.
    /// </summary>
    public static IServiceCollection AddFinanceiroModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<FinanceiroDbContext>(options => options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceiroDbContext.Schema)));

        services.AddScoped<ICategoriasRepository, CategoriasRepository>();
        services.AddScoped<IContasRepository, ContasRepository>();
        services.AddScoped<IFormasDePagamentoRepository, FormasDePagamentoRepository>();
        services.AddScoped<IHistoricoDaPessoa, HistoricoDaPessoaNoFinanceiro>();
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
