namespace Impa.Modules.Acesso.Application.RecuperacaoDeSenha;

/// <summary>Primitivas da recuperação de senha por e-mail (RN-1.11), implementadas na Infrastructure com o Identity.</summary>
public interface IRecuperacaoDeSenha
{
    /// <summary>Validade do link (RN-1.11).</summary>
    public static readonly TimeSpan Validade = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Publica <see cref="RecuperacaoDeSenhaSolicitada"/> para cada usuário <b>ativo com e-mail</b> cujo login ou
    /// e-mail é o informado (e-mail repetido: um evento por usuário). Sem nenhum, não faz nada — e não diz.
    /// Usuário com outro pedido pendente ou entregue há pouco (intervalo mínimo, D2-a da revisão 1) também é ignorado.
    /// </summary>
    Task SolicitarAsync(string usuarioOuEmail, CancellationToken ct);

    /// <summary>
    /// Dados do e-mail com um link novo para o usuário, ou <c>null</c> se não houver o que enviar: o usuário não está
    /// mais ativo com e-mail (desativado ou e-mail apagado entre o pedido e o envio) ou o pedido feito em
    /// <paramref name="pedidoEm"/> passou da idade máxima (D2-a da revisão 1).
    /// </summary>
    Task<EnvioDoLink?> PrepararEnvioAsync(Guid usuarioId, DateTimeOffset pedidoEm, CancellationToken ct);

    /// <summary>
    /// Troca a senha pelo link: valida o token (vencido, já usado, forjado, de usuário desativado ou enviado a um
    /// e-mail que não é mais o dele → <see cref="ResultadoDaRedefinicao.LinkInvalido"/>), troca a senha e o security
    /// stamp (o link deixa de valer), encerra as sessões abertas e zera a contagem de bloqueio.
    /// </summary>
    Task<ResultadoDaRedefinicao> RedefinirAsync(string token, string novaSenha, CancellationToken ct);
}

/// <summary>O que o e-mail de recuperação precisa: destinatário, link e marca.</summary>
public sealed record EnvioDoLink(
    string Email, string Nome, string Login, string Link, TimeSpan Validade, string NomeDaEmpresa, string UrlDoLogo)
{
    public override string ToString() => $"{nameof(EnvioDoLink)} {{ Login = {Login}, Link = *** }}";
}

/// <param name="UsuarioId">Dono do link, quando a senha foi redefinida (para encerrar a sessão deste navegador).</param>
public sealed record ResultadoDaRedefinicao(bool LinkInvalido, IReadOnlyList<string> ErrosDaSenha, Guid? UsuarioId = null)
{
    public static readonly ResultadoDaRedefinicao Invalido = new(true, []);

    public static ResultadoDaRedefinicao Sucesso(Guid usuarioId) => new(false, [], usuarioId);

    public static ResultadoDaRedefinicao SenhaRecusada(IReadOnlyList<string> erros) => new(false, erros);
}
