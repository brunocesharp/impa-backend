namespace Impa.Modules.Acesso.Infrastructure.RecuperacaoDeSenha;

/// <summary>Seção <c>Acesso:RecuperacaoDeSenha</c>.</summary>
public sealed class OpcoesDaRecuperacaoDeSenha
{
    public const string Secao = "Acesso:RecuperacaoDeSenha";

    /// <summary>
    /// Endereço público da SPA (ex.: <c>https://homolog.impactopapelaria.com.br</c>): base do link
    /// <c>/redefinir-senha#token=...</c> e do logotipo do e-mail. Fora de Development/Testing, só <c>https</c>.
    /// </summary>
    public string UrlDaAplicacao { get; set; } = string.Empty;

    /// <summary>
    /// D2-a da revisão 1 (N): um pedido novo para o mesmo usuário, com outro pendente ou entregue há menos que isso,
    /// é ignorado (mesma resposta, nenhum e-mail novo).
    /// </summary>
    public int IntervaloMinimoEntrePedidosEmMinutos { get; set; } = 5;

    /// <summary>D2-a da revisão 1 (M): pedido mais antigo que isso, na hora do envio, é descartado sem e-mail.</summary>
    public int IdadeMaximaDoPedidoEmMinutos { get; set; } = 60;

    internal string Base => UrlDaAplicacao.TrimEnd('/');

    internal TimeSpan IntervaloMinimoEntrePedidos => TimeSpan.FromMinutes(IntervaloMinimoEntrePedidosEmMinutos);

    internal TimeSpan IdadeMaximaDoPedido => TimeSpan.FromMinutes(IdadeMaximaDoPedidoEmMinutos);
}
