using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Impa.Shared.Email;

public static class EmailServiceCollectionExtensions
{
    /// <summary>
    /// Registra o <see cref="IEnvioDeEmail"/> (idempotente): o adaptador do Brevo quando há chave da API, senão o
    /// <see cref="EnvioDeEmailDesligado"/> — que descarta (com <paramref name="descartarSemChave"/>, em desenvolvimento
    /// e testes) ou falha cada envio, sem derrubar a API.
    /// </summary>
    public static IServiceCollection AddEnvioDeEmail(this IServiceCollection services, IConfiguration configuracao, bool descartarSemChave = true)
    {
        if (services.Any(d => d.ServiceType == typeof(IEnvioDeEmail)))
            return services;

        var secao = configuracao.GetSection(OpcoesDeEmail.Secao);
        services.AddOptions<OpcoesDeEmail>().Bind(secao);
        var opcoes = secao.Get<OpcoesDeEmail>() ?? new OpcoesDeEmail();

        if (string.IsNullOrWhiteSpace(opcoes.Brevo.ChaveDaApi))
        {
            services.AddSingleton<IEnvioDeEmail>(sp => new EnvioDeEmailDesligado(
                sp.GetRequiredService<ILogger<EnvioDeEmailDesligado>>(), descartarSemChave));
            return services;
        }

        if (string.IsNullOrWhiteSpace(opcoes.Remetente))
            throw new InvalidOperationException("Email:Remetente não configurado: o Brevo exige o remetente.");

        services.AddHttpClient<IEnvioDeEmail, BrevoAdapter>(http =>
        {
            http.BaseAddress = new Uri(opcoes.Brevo.UrlBase.EndsWith('/') ? opcoes.Brevo.UrlBase : opcoes.Brevo.UrlBase + "/");
            http.Timeout = TimeSpan.FromSeconds(opcoes.Brevo.TimeoutEmSegundos);
        });
        return services;
    }
}
