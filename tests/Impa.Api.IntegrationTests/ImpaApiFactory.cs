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

    /// <summary>Hasher do Identity com contador de conferências (tempo do login de usuário inexistente).</summary>
    public HasherContador Hasher { get; } = new();

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
            services.RemoveAll<IPasswordHasher<Usuario>>();
            services.AddSingleton<IPasswordHasher<Usuario>>(Hasher);
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

    /// <summary>
    /// Cliente HTTPS (o cookie de sessão é Secure) que guarda cookies e não segue redirecionamentos.
    /// <paramref name="https"/> = false simula a chamada direta por HTTP (healthcheck interno).
    /// </summary>
    public HttpClient CriarCliente(bool https = true) => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri(https ? "https://localhost" : "http://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    });
}

/// <summary><see cref="PasswordHasher{TUser}"/> padrão que conta os hashes calculados e as conferências de senha.</summary>
public sealed class HasherContador : IPasswordHasher<Usuario>
{
    private readonly PasswordHasher<Usuario> _hasher = new();
    private int _hashes;
    private int _conferencias;

    public int Hashes => _hashes;

    public int Conferencias => _conferencias;

    public void Zerar()
    {
        Interlocked.Exchange(ref _hashes, 0);
        Interlocked.Exchange(ref _conferencias, 0);
    }

    public string HashPassword(Usuario user, string password)
    {
        Interlocked.Increment(ref _hashes);
        return _hasher.HashPassword(user, password);
    }

    public PasswordVerificationResult VerifyHashedPassword(Usuario user, string hashedPassword, string providedPassword)
    {
        Interlocked.Increment(ref _conferencias);
        return _hasher.VerifyHashedPassword(user, hashedPassword, providedPassword);
    }
}
