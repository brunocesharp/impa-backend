using Impa.Modules.Acesso.Infrastructure.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace Impa.Api.IntegrationTests;

/// <summary>
/// API real (Program do Host) contra um PostgreSQL em container. Um banco por classe de teste.
/// </summary>
public sealed class ImpaApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminUsuario = "admin";
    public const string AdminSenha = "admin-homolog-2026";
    public const string AdminNome = "Administrador IMPA";
    public const string NomeEmpresa = "Impacto Papelaria";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("impa")
        .WithUsername("impa")
        .WithPassword("impa")
        .Build();

    /// <summary>Relógio controlado pelos testes (expiração da sessão).</summary>
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    public string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync() => await _postgres.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Impa", _postgres.GetConnectionString());
        builder.UseSetting("IMPA_ADMIN_USUARIO", AdminUsuario);
        builder.UseSetting("IMPA_ADMIN_SENHA", AdminSenha);
        builder.UseSetting("IMPA_ADMIN_NOME", AdminNome);
        builder.UseSetting("Empresa:Nome", NomeEmpresa);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    /// <summary>Cria, se ainda não existir, um usuário com o perfil indicado (ex.: a Ana do cenário 1.1).</summary>
    public async Task CriarUsuarioAsync(string usuario, string senha, string nome, string perfil)
    {
        _ = CriarCliente(); // garante o Host no ar (migrações e seed)
        await using var escopo = Services.CreateAsyncScope();
        var usuarios = escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        if (await usuarios.FindByNameAsync(usuario) is not null)
            return;

        var novo = new Usuario { UserName = usuario, NomeExibicao = nome };
        var criado = await usuarios.CreateAsync(novo, senha);
        Assert.True(criado.Succeeded, string.Join("; ", criado.Errors.Select(e => e.Description)));
        Assert.True((await usuarios.AddToRoleAsync(novo, perfil)).Succeeded);
    }

    /// <summary>Cliente HTTPS (o cookie de sessão é Secure) que guarda cookies e não segue redirecionamentos.</summary>
    public HttpClient CriarCliente() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    });
}
