using Impa.Modules.Acesso.Infrastructure.Identity;
using Impa.Modules.Acesso.Infrastructure.Persistence;
using Impa.Modules.Pessoas.Infrastructure.Persistence;
using Impa.Modules.Configuracao.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Impa.Host.Persistence;

public static class DatabaseMigrations
{
    /// <summary>
    /// Prepara o banco na inicialização: aplica as migrações do Host (extensões) e de cada módulo, nesta ordem,
    /// e roda os seeds (perfis padrão e primeiro administrador). Desligável por <c>Database:MigrateOnStartup</c>.
    /// </summary>
    public static async Task InicializarBancoAsync(this WebApplication app)
    {
        if (!app.Configuration.GetValue("Database:MigrateOnStartup", true))
            return;

        await using var scope = app.Services.CreateAsyncScope();
        var servicos = scope.ServiceProvider;

        await servicos.GetRequiredService<HostDbContext>().Database.MigrateAsync();
        await servicos.GetRequiredService<AcessoDbContext>().Database.MigrateAsync();
        await servicos.GetRequiredService<PessoasDbContext>().Database.MigrateAsync();
        await servicos.GetRequiredService<ConfiguracaoDbContext>().Database.MigrateAsync();

        await AcessoSeed.ExecutarAsync(servicos, AdministradorInicialDaConfiguracao(app.Configuration));
    }

    private static AdministradorInicial? AdministradorInicialDaConfiguracao(IConfiguration configuracao)
    {
        var usuario = configuracao["IMPA_ADMIN_USUARIO"];
        var senha = configuracao["IMPA_ADMIN_SENHA"];
        var nome = configuracao["IMPA_ADMIN_NOME"];

        return string.IsNullOrWhiteSpace(usuario) || string.IsNullOrWhiteSpace(senha)
            ? null
            : new AdministradorInicial(usuario, senha, string.IsNullOrWhiteSpace(nome) ? usuario : nome);
    }
}
