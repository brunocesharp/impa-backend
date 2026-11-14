namespace Impa.Modules.Acesso.Application.RecuperacaoDeSenha;

/// <summary>Primitivas da recuperação de senha por e-mail (RN-1.11), implementadas na Infrastructure com o Identity.</summary>
public interface IRecuperacaoDeSenha
{
    /// <summary>Validade do link (RN-1.11).</summary>
    public static readonly TimeSpan Validade = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Publica <see cref="RecuperacaoDeSenhaSolicitada"/> para cada usuário <b>ativo com e-mail</b> cujo login ou
    /// e-mail é o informado (e-mail repetido: um evento por usuário). Sem nenhum, não faz nada — e não diz.
    /// </summary>
    Task SolicitarAsync(string usuarioOuEmail, CancellationToken ct);

    /// <summary>
    /// Dados do e-mail com um link novo para o usuário, ou <c>null</c> se ele não estiver mais ativo com e-mail
    /// (desativado ou e-mail apagado entre o pedido e o envio).
    /// </summary>
    Task<EnvioDoLink?> PrepararEnvioAsync(Guid usuarioId, CancellationToken ct);

    /// <summary>
    /// Troca a senha pelo link: valida o token (vencido, já usado, forjado ou de usuário desativado →
    /// <see cref="ResultadoDaRedefinicao.LinkInvalido"/>), troca a senha e o security stamp (o link deixa de valer),
    /// encerra as sessões abertas e zera a contagem de bloqueio.
    /// </summary>
    Task<ResultadoDaRedefinicao> RedefinirAsync(string token, string novaSenha, CancellationToken ct);
}

/// <summary>O que o e-mail de recuperação precisa: destinatário, link e marca.</summary>
public sealed record EnvioDoLink(
    string Email, string Nome, string Login, string Link, TimeSpan Validade, string NomeDaEmpresa, string UrlDoLogo)
{
    public override string ToString() => $"{nameof(EnvioDoLink)} {{ Login = {Login}, Link = *** }}";
}

public sealed record ResultadoDaRedefinicao(bool LinkInvalido, IReadOnlyList<string> ErrosDaSenha)
{
    public static readonly ResultadoDaRedefinicao Sucesso = new(false, []);
    public static readonly ResultadoDaRedefinicao Invalido = new(true, []);

    public static ResultadoDaRedefinicao SenhaRecusada(IReadOnlyList<string> erros) => new(false, erros);
}
