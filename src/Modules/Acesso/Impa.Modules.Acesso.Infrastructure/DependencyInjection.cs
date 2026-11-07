using Impa.Modules.Acesso.Application.Autenticacao;
using Impa.Modules.Acesso.Application.Autenticacao.Commands.TrocarSenha;
using Impa.Modules.Acesso.Application.Perfis;
using Impa.Modules.Acesso.Application.Terminais;
using Impa.Modules.Acesso.Application.Usuarios;
using Impa.Modules.Acesso.Contracts;
using Impa.Modules.Acesso.Infrastructure.Identity;
using Impa.Modules.Acesso.Infrastructure.Persistence;
using Impa.Modules.Acesso.Infrastructure.Sessoes;
using Impa.Modules.Acesso.Infrastructure.Terminais;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Impa.Modules.Acesso.Infrastructure;

public static class DependencyInjection
{
    /// <summary>RN-1.2: senhas erradas seguidas até o bloqueio.</summary>
    public const int MaximoDeSenhasErradas = 5;

    /// <summary>RN-1.2: duração do bloqueio por tentativas.</summary>
    public static readonly TimeSpan DuracaoDoBloqueio = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Registra o módulo Acesso: banco, Identity (usuários, perfis, SignInManager) e a sessão.
    /// O esquema de autenticação por cookie é configurado pelo Host.
    /// </summary>
    public static IdentityBuilder AddAcessoModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AcessoDbContext>(options => options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", AcessoDbContext.Schema)));

        services.AddHttpContextAccessor();
        services.AddSingleton<HashFicticio>();
        services.AddScoped<ISessaoService, SessaoService>();
        services.AddScoped<GestaoUsuarios>();
        services.AddScoped<IGestaoUsuarios>(s => s.GetRequiredService<GestaoUsuarios>());
        services.AddScoped<ICadastroUsuarios>(s => s.GetRequiredService<GestaoUsuarios>());
        services.AddScoped<ITrocaDeSenha, TrocaDeSenha>();
        services.AddScoped<IGestaoPerfis, GestaoPerfis>();
        services.AddScoped<ITerminais, GestaoTerminais>();
        services.AddSingleton<CacheDePermissoes>();
        services.AddScoped<IPermissoesDoUsuario, PermissoesDoUsuario>();
        services.AddScoped<IUsuariosComPermissao, UsuariosComPermissao>(); // contrato público (story 4.2)
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<SessaoTicketStore>(); // sessões no banco; ligado ao cookie pelo Host

        // Limpeza das sessões antigas (story 1-11); a tarefa periódica fica no Host.
        services.AddOptions<LimpezaDeSessoesOptions>()
            .BindConfiguration(LimpezaDeSessoesOptions.Secao)
            .Validate(o => o.RetencaoEmDias >= 1 && o.IntervaloEmHoras >= 1,
                "Acesso:LimpezaDeSessoes: retenção e intervalo precisam ser positivos.")
            .ValidateOnStart();
        services.AddScoped<ILimpezaDeSessoes, LimpezaDeSessoes>();

        return services
            .AddIdentityCore<Usuario>(options =>
            {
                // RN-1.1: mínimo de 8 caracteres, sem exigir dígito, símbolo ou maiúscula.
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireLowercase = false;
                options.Password.RequiredUniqueChars = 1;
                options.User.RequireUniqueEmail = false;

                // RN-1.2: 5 senhas erradas seguidas bloqueiam por 15 minutos, para todos os usuários
                // (os já existentes foram ligados pela migração BloqueioESessoes).
                options.Lockout.MaxFailedAccessAttempts = MaximoDeSenhasErradas;
                options.Lockout.DefaultLockoutTimeSpan = DuracaoDoBloqueio;
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddRoles<Perfil>()
            .AddEntityFrameworkStores<AcessoDbContext>()
            .AddSignInManager();
    }

    /// <summary>
    /// Prepara o módulo antes da primeira requisição (chamado pelo Host depois do <c>Build</c>):
    /// calcula o hash fictício do login de usuário inexistente.
    /// </summary>
    public static void PrepararAcesso(this IServiceProvider servicos) =>
        servicos.GetRequiredService<HashFicticio>().Preparar(servicos);
}
