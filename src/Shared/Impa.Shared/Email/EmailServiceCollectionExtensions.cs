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

        // R9 (revisão 1 da story 1-8, ADR-009): configuração incompleta não derruba a API. Cada envio registra o erro
        // no log e falha, ficando "com falha" no Outbox para reprocessar depois de configurar o remetente.
        if (string.IsNullOrWhiteSpace(opcoes.Remetente))
        {
            services.AddSingleton<IEnvioDeEmail>(sp => new EnvioDeEmailDesligado(
                sp.GetRequiredService<ILogger<EnvioDeEmailDesligado>>(), descartar: false,
                motivo: "Email:Remetente não configurado (Email__Remetente): o Brevo exige o remetente"));
            return services;
        }

        services.AddHttpClient<IEnvioDeEmail, BrevoAdapter>(http =>
        {
            http.BaseAddress = new Uri(opcoes.Brevo.UrlBase.EndsWith('/') ? opcoes.Brevo.UrlBase : opcoes.Brevo.UrlBase + "/");
            http.Timeout = TimeSpan.FromSeconds(opcoes.Brevo.TimeoutEmSegundos);
        });
        return services;
    }
}
