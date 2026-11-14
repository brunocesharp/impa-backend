namespace Impa.Modules.Acesso.Infrastructure.RecuperacaoDeSenha;

/// <summary>Seção <c>Acesso:RecuperacaoDeSenha</c>.</summary>
public sealed class OpcoesDaRecuperacaoDeSenha
{
    public const string Secao = "Acesso:RecuperacaoDeSenha";

    /// <summary>
    /// Endereço público da SPA (ex.: <c>https://homolog.impactopapelaria.com.br</c>): base do link
    /// <c>/redefinir-senha#token=...</c> e do logotipo do e-mail.
    /// </summary>
    public string UrlDaAplicacao { get; set; } = string.Empty;

    internal string Base => UrlDaAplicacao.TrimEnd('/');
}
