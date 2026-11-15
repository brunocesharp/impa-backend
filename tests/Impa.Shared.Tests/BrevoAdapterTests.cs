using System.Net;
using System.Text.Json;
using Impa.Shared.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Impa.Shared.Tests;

/// <summary>Story 1-8, T1 — adaptador da API transacional do Brevo (ADR-009).</summary>
public class BrevoAdapterTests
{
    private static readonly OpcoesDeEmail Opcoes = new()
    {
        Remetente = "nao-responda@exemplo.com.br",
        NomeDoRemetente = "IMPA",
        Brevo = new OpcoesDoBrevo { ChaveDaApi = "chave-de-teste", UrlBase = "https://api.brevo.test/v3/" },
    };

    private static readonly MensagemDeEmail Mensagem = new(
        "ana@lojaexemplo.com.br", "Ana", "Redefinição de senha", "<p>Olá</p>", "Olá");

    private static (BrevoAdapter adapter, ManipuladorFalso http) Criar(HttpStatusCode status, string corpo = """{"messageId":"<1@relay>"}""")
    {
        var manipulador = new ManipuladorFalso(status, corpo);
        var cliente = new HttpClient(manipulador) { BaseAddress = new Uri(Opcoes.Brevo.UrlBase) };
        return (new BrevoAdapter(cliente, Options.Create(Opcoes), NullLogger<BrevoAdapter>.Instance), manipulador);
    }

    [Fact]
    public async Task Envia_para_smtp_email_com_a_chave_no_cabecalho_e_o_remetente_configurado()
    {
        var (adapter, http) = Criar(HttpStatusCode.Created);

        await adapter.EnviarAsync(Mensagem, CancellationToken.None);

        var requisicao = Assert.Single(http.Requisicoes);
        Assert.Equal(HttpMethod.Post, requisicao.Metodo);
        Assert.Equal("https://api.brevo.test/v3/smtp/email", requisicao.Url);
        Assert.Equal("chave-de-teste", requisicao.ChaveDaApi);

        using var json = JsonDocument.Parse(requisicao.Corpo);
        var raiz = json.RootElement;
        Assert.Equal("nao-responda@exemplo.com.br", raiz.GetProperty("sender").GetProperty("email").GetString());
        Assert.Equal("IMPA", raiz.GetProperty("sender").GetProperty("name").GetString());
        var para = Assert.Single(raiz.GetProperty("to").EnumerateArray());
        Assert.Equal("ana@lojaexemplo.com.br", para.GetProperty("email").GetString());
        Assert.Equal("Ana", para.GetProperty("name").GetString());
        Assert.Equal("Redefinição de senha", raiz.GetProperty("subject").GetString());
        Assert.Equal("<p>Olá</p>", raiz.GetProperty("htmlContent").GetString());
        Assert.Equal("Olá", raiz.GetProperty("textContent").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Resposta_de_erro_vira_falha_no_envio_para_o_despachante_tentar_de_novo(HttpStatusCode status)
    {
        var (adapter, _) = Criar(status, """{"code":"x","message":"erro"}""");

        var falha = await Assert.ThrowsAsync<FalhaNoEnvioDeEmail>(() => adapter.EnviarAsync(Mensagem, CancellationToken.None));

        Assert.Contains(((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture), falha.Message);
        Assert.DoesNotContain("chave-de-teste", falha.Message);
    }

    [Fact]
    public async Task Sem_nome_do_destinatario_nao_manda_o_campo_name()
    {
        var (adapter, http) = Criar(HttpStatusCode.Created);

        await adapter.EnviarAsync(Mensagem with { NomeDoDestinatario = null }, CancellationToken.None);

        using var json = JsonDocument.Parse(Assert.Single(http.Requisicoes).Corpo);
        var para = Assert.Single(json.RootElement.GetProperty("to").EnumerateArray());
        Assert.False(para.TryGetProperty("name", out _));
    }

    [Theory]
    [InlineData("nao e json")]
    [InlineData("")]
    public async Task Resposta_2xx_com_corpo_ilegivel_e_aceita_sem_messageId(string corpo)
    {
        var (adapter, _) = Criar(HttpStatusCode.Created, corpo);

        await adapter.EnviarAsync(Mensagem, CancellationToken.None);
    }

    [Fact]
    public async Task Resposta_2xx_com_falha_ao_ler_o_corpo_nunca_lanca_para_nao_reenviar()
    {
        // R8 (revisão 1): o Brevo já aceitou; uma falha qualquer na leitura (aqui, a conexão caindo no meio do corpo)
        // não pode virar exceção, senão o despachante manda o mesmo e-mail de novo.
        var manipulador = new ManipuladorFalso(HttpStatusCode.Created, conteudo: () => new ConteudoQueFalha());
        var cliente = new HttpClient(manipulador) { BaseAddress = new Uri(Opcoes.Brevo.UrlBase) };
        var adapter = new BrevoAdapter(cliente, Options.Create(Opcoes), NullLogger<BrevoAdapter>.Instance);

        await adapter.EnviarAsync(Mensagem, CancellationToken.None);
    }

    [Fact]
    public void Timeout_padrao_do_Brevo_e_5_segundos()
    {
        // R15 (D1-a): o despachante único espera no máximo isso por evento com o Brevo fora do ar.
        Assert.Equal(5, new OpcoesDoBrevo().TimeoutEmSegundos);
    }

    [Fact]
    public async Task Envio_desligado_nao_lanca()
    {
        var desligado = new EnvioDeEmailDesligado(NullLogger<EnvioDeEmailDesligado>.Instance);

        await desligado.EnviarAsync(Mensagem, CancellationToken.None);
    }

    private sealed record RequisicaoFeita(HttpMethod Metodo, string Url, string? ChaveDaApi, string Corpo);

    private sealed class ManipuladorFalso(HttpStatusCode status, string corpo = "", Func<HttpContent>? conteudo = null) : HttpMessageHandler
    {
        public List<RequisicaoFeita> Requisicoes { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            request.Headers.TryGetValues("api-key", out var chaves);
            Requisicoes.Add(new RequisicaoFeita(request.Method, request.RequestUri!.ToString(), chaves?.SingleOrDefault(),
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct)));
            return new HttpResponseMessage(status) { Content = conteudo?.Invoke() ?? new StringContent(corpo) };
        }
    }

    /// <summary>Corpo cuja leitura falha como uma conexão que cai no meio da resposta.</summary>
    private sealed class ConteudoQueFalha : HttpContent
    {
        public ConteudoQueFalha() => Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new IOException("conexão encerrada (teste)");

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
