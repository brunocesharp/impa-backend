using FluentValidation;
using Impa.Host.Endpoints;
using Impa.Host.Persistence;
using Impa.Host.Pipeline;
using Impa.Host.Seguranca;
using Impa.Modules.Acesso.Application;
using Impa.Modules.Acesso.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Impa")
    ?? throw new InvalidOperationException("ConnectionStrings:Impa não configurada.");

builder.Services.AddDbContext<HostDbContext>(options => options.UseNpgsql(
    connectionString,
    npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", HostDbContext.Schema)));

// Módulos
builder.Services.AddAcessoModule(connectionString);

// Casos de uso: handlers (MediatR) e validators (FluentValidation) de cada módulo
builder.Services.AddMediatR(cfg =>
{
    cfg.LicenseKey = builder.Configuration["MediatR:LicenseKey"];
    cfg.RegisterServicesFromAssembly(AcessoApplication.Assembly);
    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
});
builder.Services.AddValidatorsFromAssembly(AcessoApplication.Assembly);

// Segurança
// Chaves que criptografam o cookie de sessão e o antiforgery. No container ficam num volume
// (DataProtection:KeysPath); sem persistir, cada nova versão derrubaria todas as sessões.
var chavesDataProtection = builder.Services.AddDataProtection().SetApplicationName("impa");
if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } caminhoChaves)
    chavesDataProtection.PersistKeysToFileSystem(new DirectoryInfo(caminhoChaves));

builder.Services.AddAutenticacaoPorCookie();
builder.Services.AddAutorizacaoPorPermissao();
builder.Services.AddProtecaoCsrf();

// Atrás do Caddy (TLS termina no proxy): esquema e IP do cliente vêm dos cabeçalhos X-Forwarded-*.
// A API só é alcançável pela rede interna do compose, então qualquer proxy dessa rede é confiável.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks().AddDbContextCheck<HostDbContext>("banco");

var app = builder.Build();

await app.InicializarBancoAsync();

app.UseForwardedHeaders();
app.UseAuthentication();
app.UseProtecaoCsrf();
app.UseAuthorization();

app.MapHealthChecks("/api/health").AllowAnonymous();
app.MapAutenticacaoEndpoints();
app.MapConfiguracaoEndpoints();
app.MapVersaoEndpoints();

app.Run();

public partial class Program;
