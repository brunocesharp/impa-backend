using System.Globalization;
using System.Net;

namespace Impa.Api.IntegrationTests;

/// <summary>T3/T5 — sessão de no máximo 12 horas, sem expiração deslizante (CA-4, RN-1.3).</summary>
public class SessaoTests(ImpaApiFactory factory) : IClassFixture<ImpaApiFactory>
{
    [Fact]
    public async Task Sessao_termina_12_horas_apos_o_login_mesmo_com_uso_continuo()
    {
        var cliente = await ClienteApi.AbrirAsync(factory);
        await cliente.EntrarAsync(ImpaApiFactory.AdminUsuario, ImpaApiFactory.AdminSenha);

        // Uso ao longo do dia: com expiração deslizante, cada uso depois da metade empurraria o fim.
        foreach (var horas in new[] { 1, 6, 11 })
        {
            factory.Clock.SetUtcNow(factory.Clock.Start.AddHours(horas));
            Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/api/auth/me")).StatusCode);
        }

        factory.Clock.SetUtcNow(factory.Clock.Start.AddHours(12).AddMinutes(-1));
        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/api/auth/me")).StatusCode);

        factory.Clock.SetUtcNow(factory.Clock.Start.AddHours(12).AddMinutes(1));
        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Cookie_de_sessao_e_persistente_e_expira_12_horas_apos_o_login()
    {
        var cliente = await ClienteApi.AbrirAsync(factory);
        var momentoDoLogin = factory.Clock.GetUtcNow();
        await cliente.EntrarAsync(ImpaApiFactory.AdminUsuario, ImpaApiFactory.AdminSenha);

        // Persistente (sobrevive a fechar o navegador/PWA), mas com o mesmo limite de 12 h do tíquete
        // (decisão do usuário em 01/11/2026, achado A18).
        var atributos = cliente.UltimosSetCookie["impa.sessao"].Split(';', StringSplitOptions.TrimEntries);
        var expires = atributos.Single(a => a.StartsWith("expires=", StringComparison.OrdinalIgnoreCase));
        var expira = DateTimeOffset.Parse(expires["expires=".Length..], CultureInfo.InvariantCulture);

        Assert.InRange(expira, momentoDoLogin.AddHours(12).AddSeconds(-2), momentoDoLogin.AddHours(12).AddSeconds(2));
    }
}
