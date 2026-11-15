using Impa.Shared.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Impa.Shared.Tests;

/// <summary>Story 1-8, T1 — qual envio de e-mail é registrado, conforme a configuração.</summary>
public class EnvioDeEmailRegistroTests
{
    private static readonly MensagemDeEmail Mensagem = new("ana@lojaexemplo.com.br", "Ana", "Assunto", "<p>x</p>", "x");

    private static IServiceProvider Montar(Dictionary<string, string?> valores, bool descartarSemChave = true)
    {
        var configuracao = new ConfigurationBuilder().AddInMemoryCollection(valores).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddEnvioDeEmail(configuracao, descartarSemChave);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Sem_chave_da_api_o_envio_fica_desligado()
    {
        Assert.IsType<EnvioDeEmailDesligado>(Montar([]).GetRequiredService<IEnvioDeEmail>());
    }

    [Fact]
    public void Com_chave_e_remetente_usa_o_Brevo()
    {
        var servicos = Montar(new()
        {
            ["Email:Remetente"] = "nao-responda@exemplo.com.br",
            ["Email:Brevo:ChaveDaApi"] = "chave",
        });

        Assert.IsType<BrevoAdapter>(servicos.GetRequiredService<IEnvioDeEmail>());
    }

    [Fact]
    public async Task Com_chave_e_sem_remetente_a_API_sobe_e_cada_envio_falha_para_ficar_no_outbox()
    {
        // R9 (revisão 1, ADR-009): configuração incompleta do e-mail não derruba a API.
        var envio = Montar(new() { ["Email:Brevo:ChaveDaApi"] = "chave" }).GetRequiredService<IEnvioDeEmail>();

        var falha = await Assert.ThrowsAsync<FalhaNoEnvioDeEmail>(() => envio.EnviarAsync(Mensagem, CancellationToken.None));
        Assert.Contains("Email__Remetente", falha.Message);
    }

    [Fact]
    public async Task Sem_chave_em_desenvolvimento_descarta_o_envio()
    {
        await Montar([]).GetRequiredService<IEnvioDeEmail>().EnviarAsync(Mensagem, CancellationToken.None);
    }

    [Fact]
    public async Task Sem_chave_fora_de_desenvolvimento_cada_envio_falha_para_ficar_no_outbox()
    {
        var envio = Montar([], descartarSemChave: false).GetRequiredService<IEnvioDeEmail>();

        var falha = await Assert.ThrowsAsync<FalhaNoEnvioDeEmail>(() => envio.EnviarAsync(Mensagem, CancellationToken.None));
        Assert.Contains("Email__Brevo__ChaveDaApi", falha.Message);
    }
}
