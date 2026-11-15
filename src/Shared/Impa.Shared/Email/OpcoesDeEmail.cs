namespace Impa.Shared.Email;

/// <summary>
/// Seção <c>Email</c> da configuração. A chave da API é segredo: vem da variável de ambiente
/// <c>Email__Brevo__ChaveDaApi</c> (segredo do Docker na VPS), nunca do repositório.
/// </summary>
public sealed class OpcoesDeEmail
{
    public const string Secao = "Email";

    /// <summary>Endereço remetente, do domínio autenticado no Brevo (SPF, DKIM, DMARC).</summary>
    public string Remetente { get; set; } = string.Empty;

    public string NomeDoRemetente { get; set; } = "IMPA";

    public OpcoesDoBrevo Brevo { get; set; } = new();
}

public sealed class OpcoesDoBrevo
{
    /// <summary>Sem chave, o envio fica desligado (<see cref="EnvioDeEmailDesligado"/>): desenvolvimento e testes.</summary>
    public string ChaveDaApi { get; set; } = string.Empty;

    public string UrlBase { get; set; } = "https://api.brevo.com/v3/";

    /// <summary>
    /// Curto de propósito (D1-a da revisão 1 da story 1-8): a chamada roda no despachante único do Outbox, que
    /// espera até isso por evento com o Brevo fora do ar. O isolamento por módulo fica com o enabler novo do épico 1.
    /// </summary>
    public int TimeoutEmSegundos { get; set; } = 5;
}
