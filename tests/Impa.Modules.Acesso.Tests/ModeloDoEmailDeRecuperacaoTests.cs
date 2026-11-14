using Impa.Modules.Acesso.Application.RecuperacaoDeSenha;

namespace Impa.Modules.Acesso.Tests;

/// <summary>Story 1-8, T6 — modelo do e-mail de redefinição (logotipo, texto curto, botão, validade de 30 minutos).</summary>
public class ModeloDoEmailDeRecuperacaoTests
{
    private static readonly EnvioDoLink Dados = new(
        "ana@lojaexemplo.com.br", "Ana <Souza>", "ana", "https://impa.test/redefinir-senha#token=abc-123",
        TimeSpan.FromMinutes(30), "Impacto Papelaria", "https://impa.test/brand/logo-horizontal.png");

    [Fact]
    public void Monta_para_o_usuario_com_assunto_fixo()
    {
        var email = ModeloDoEmailDeRecuperacao.Montar(Dados);

        Assert.Equal("ana@lojaexemplo.com.br", email.Para);
        Assert.Equal("Ana <Souza>", email.NomeDoDestinatario);
        Assert.Equal("Redefinição de senha", email.Assunto);
    }

    [Fact]
    public void Html_tem_logotipo_botao_com_o_link_e_aviso_de_validade()
    {
        var html = ModeloDoEmailDeRecuperacao.Montar(Dados).Html;

        Assert.Contains("""<img src="https://impa.test/brand/logo-horizontal.png" alt="Impacto Papelaria" """, html);
        Assert.Contains("""<a href="https://impa.test/redefinir-senha#token=abc-123" """, html);
        Assert.Contains("Definir nova senha", html);
        Assert.Contains("30 minutos", html);
        Assert.Contains("só pode ser usado uma vez", html);
    }

    [Fact]
    public void Html_escapa_o_nome_do_usuario()
    {
        var html = ModeloDoEmailDeRecuperacao.Montar(Dados).Html;

        Assert.Contains("Ana &lt;Souza&gt;", html);
        Assert.DoesNotContain("Ana <Souza>", html);
    }

    [Fact]
    public void Texto_puro_tem_o_link_o_login_e_a_validade()
    {
        var texto = ModeloDoEmailDeRecuperacao.Montar(Dados).Texto;

        Assert.Contains("https://impa.test/redefinir-senha#token=abc-123", texto);
        Assert.Contains("usuário ana", texto);
        Assert.Contains("30 minutos", texto);
    }

    [Fact]
    public void Dados_do_envio_nao_mostram_o_link_no_log()
    {
        Assert.DoesNotContain("abc-123", Dados.ToString());
    }
}
