using FluentValidation;
using Impa.Host.Endpoints;
using Impa.Host.Messaging;
using Impa.Host.Persistence;
using Impa.Host.Pipeline;
using Impa.Host.Seguranca;
using Impa.Host.Tarefas;
using Impa.Modules.Acesso.Application;
using Impa.Modules.Acesso.Infrastructure;
using Impa.Modules.Pessoas.Application;
using Impa.Modules.Pessoas.Infrastructure;
using Impa.Modules.Configuracao.Application;
using Impa.Modules.Configuracao.Infrastructure;
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
builder.Services.AddPessoasModule(connectionString);
builder.Services.AddConfiguracaoModule(connectionString);

// Casos de uso: handlers (MediatR) e validators (FluentValidation) de cada módulo
builder.Services.AddMediatR(cfg =>
{
    cfg.LicenseKey = builder.Configuration["MediatR:LicenseKey"];
    cfg.RegisterServicesFromAssembly(AcessoApplication.Assembly);
    cfg.RegisterServicesFromAssembly(PessoasApplication.Assembly);
    cfg.RegisterServicesFromAssembly(ConfiguracaoApplication.Assembly);
    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
});
builder.Services.AddValidatorsFromAssembly(AcessoApplication.Assembly);
builder.Services.AddValidatorsFromAssembly(PessoasApplication.Assembly);
builder.Services.AddValidatorsFromAssembly(ConfiguracaoApplication.Assembly);

// Integration events (ADR-011): despachante dos Outbox, espera por chave e reprocessamento.
// Cada módulo registra o próprio Outbox e os próprios handlers.
builder.Services.AddDespachanteDeEventos(builder.Configuration);

// Tarefas em segundo plano
builder.Services.AddHostedService<LimpezaDeSessoesTarefa>(); // sessões antigas (story 1-11)

// Segurança
// Chaves que criptografam o cookie de sessão e o antiforgery. No container ficam num volume
// (DataProtection:KeysPath); sem persistir, cada nova versão derrubaria todas as sessões.
var chavesDataProtection = builder.Services.AddDataProtection().SetApplicationName("impa");
if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } caminhoChaves)
    chavesDataProtection.PersistKeysToFileSystem(new DirectoryInfo(caminhoChaves));

builder.Services.AddAutenticacaoPorCookie();
builder.Services.AddAutorizacaoPorPermissao();
builder.Services.AddTerminalRegistrado(); // RequireTerminal: PDV e caixa só em terminal registrado (story 1-6)
builder.Services.AddCamposPorPermissao();
builder.Services.AddProtecaoCsrf();
builder.Services.AddLimiteDeLogin(builder.Configuration);

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
app.Services.PrepararAcesso();

app.UseForwardedHeaders();
app.UseLimiteDeLogin(); // depois do X-Forwarded-For: o limite é por IP do cliente
app.UseAuthentication();
app.UsePermissoesDaSessao(); // perfil e permissões atuais, do banco (story 1.3)
app.UseProtecaoCsrf();
app.UseTrocaDeSenhaObrigatoria(); // senha provisória: só a troca de senha (story 1.4, RN-1.8)
app.UseAuthorization();

app.MapHealthChecks("/api/health").AllowAnonymous();
app.MapAutenticacaoEndpoints();
app.MapConfiguracaoEndpoints();
app.MapPerfisEndpoints();
app.MapUsuariosEndpoints();
app.MapPessoasEndpoints();
app.MapTerminaisEndpoints();
app.MapMensageriaEndpoints();
app.MapNotificacoesEndpoints();
app.MapVersaoEndpoints();
foreach (var adicionais in app.Services.GetServices<IEndpointsAdicionais>())
    adicionais.Mapear(app);

app.Run();

public partial class Program;
