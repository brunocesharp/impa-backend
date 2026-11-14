using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Impa.Shared.Email;

namespace Impa.Api.IntegrationTests;

/// <summary>Adaptador falso do Brevo (story 1-8): guarda os e-mails "enviados"; pode falhar de propósito.</summary>
public sealed partial class CaixaDeEmails : IEnvioDeEmail
{
    private readonly ConcurrentQueue<MensagemDeEmail> _enviados = new();
    private readonly ConcurrentDictionary<string, int> _falhasRestantes = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<MensagemDeEmail> Enviados => _enviados.ToArray();

    /// <summary>As próximas <paramref name="vezes"/> tentativas para <paramref name="email"/> falham como o Brevo fora do ar.</summary>
    public void FalharPara(string email, int vezes = int.MaxValue) => _falhasRestantes[email] = vezes;

    public Task EnviarAsync(MensagemDeEmail mensagem, CancellationToken ct)
    {
        if (_falhasRestantes.TryGetValue(mensagem.Para, out var restantes) && restantes > 0)
        {
            _falhasRestantes[mensagem.Para] = restantes - 1;
            throw new FalhaNoEnvioDeEmail("Brevo fora do ar (teste)");
        }

        _enviados.Enqueue(mensagem);
        return Task.CompletedTask;
    }

    public IReadOnlyList<MensagemDeEmail> Para(string email) =>
        Enviados.Where(m => string.Equals(m.Para, email, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>Espera o <paramref name="quantidade"/>-ésimo e-mail para <paramref name="email"/> (o despachante é assíncrono).</summary>
    public async Task<MensagemDeEmail> EsperarAsync(string email, int quantidade = 1)
    {
        var fim = DateTime.UtcNow.AddSeconds(20);
        while (Para(email).Count < quantidade)
        {
            if (DateTime.UtcNow > fim)
                Assert.Fail($"nenhum e-mail para {email}");
            await Task.Delay(50);
        }

        return Para(email)[quantidade - 1];
    }

    /// <summary>O token do link de redefinição (<c>/redefinir-senha#token=...</c>).</summary>
    public static string TokenDoLink(MensagemDeEmail mensagem)
    {
        var achado = LinkDeRedefinicao().Match(mensagem.Texto);
        Assert.True(achado.Success, "o e-mail não tem o link de redefinição");
        return Uri.UnescapeDataString(achado.Groups[1].Value);
    }

    [GeneratedRegex(@"/redefinir-senha#token=([^\s""<]+)")]
    private static partial Regex LinkDeRedefinicao();
}
