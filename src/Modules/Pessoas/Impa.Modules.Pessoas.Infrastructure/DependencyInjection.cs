using Impa.Modules.Pessoas.Application;
using Impa.Modules.Pessoas.Application.Clientes;
using Impa.Modules.Pessoas.Application.Fornecedores;
using Impa.Modules.Pessoas.Contracts;
using Impa.Modules.Pessoas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Impa.Modules.Pessoas.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registra o módulo Pessoas (clientes e fornecedores, story 3-7): banco no schema <c>pessoas</c>, repositórios e
    /// o contrato público <see cref="IPessoasConsultas"/>. As implementações de <see cref="IHistoricoDaPessoa"/> são
    /// registradas por Vendas, Compras e Financeiro (épicos 6, 11 e 12).
    /// </summary>
    public static IServiceCollection AddPessoasModule(this IServiceCollection services, string connectionString)
    {
        // RN-14.6: sem EnableSensitiveDataLogging — os parâmetros (CPF, telefone, e-mail) não vão para o log do EF Core.
        services.AddDbContext<PessoasDbContext>(options => options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", PessoasDbContext.Schema)));

        services.AddScoped<IClientesRepository, ClientesRepository>();
        services.AddScoped<IFornecedoresRepository, FornecedoresRepository>();
        services.AddScoped<IPessoasConsultas, PessoasConsultas>();
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
