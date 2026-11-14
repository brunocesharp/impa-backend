using System.Net;
using System.Net.Http.Json;
using Impa.Modules.Acesso.Application.Perfis;
using Impa.Modules.Acesso.Infrastructure.Identity;
using Impa.Modules.Acesso.Infrastructure.Persistence;
using Impa.Shared.Messaging;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Impa.Api.IntegrationTests;

/// <summary>Story 1-8 — recuperação de senha por e-mail, com o adaptador falso do Brevo (CA-1, CA-2, CA-3).</summary>
public class RecuperacaoDeSenhaTests(ImpaApiFactory factory) : IClassFixture<ImpaApiFactory>
{
    private const string Senha = "senha-antiga-1";
    private const string NovaSenha = "novasenha26";
    private const string MensagemLinkInvalido = "Link inválido ou expirado";
    private const string TipoDoEvento = "acesso.recuperacao-de-senha-solicitada";

    private sealed record Usuario_(string Login, string? Email, Guid Id);

    /// <summary>Usuário novo com login e e-mail únicos; <paramref name="comEmail"/> = false para "sem e-mail cadastrado".</summary>
    private async Task<Usuario_> NovoUsuarioAsync(string prefixo, bool comEmail = true, bool ativo = true)
    {
        var login = $"{prefixo}-{Guid.NewGuid():N}"[..20];
        var id = await factory.CriarUsuarioAsync(login, Senha, prefixo, PerfisPadrao.OperadorDeCaixa.Nome);
        var email = comEmail ? $"{login}@lojaexemplo.com.br" : null;

        await using var escopo = factory.Services.CreateAsyncScope();
        var usuarios = escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        var usuario = (await usuarios.FindByIdAsync(id.ToString()))!;
        usuario.Email = email;
        if (!ativo)
            usuario.Desativar();
        Assert.True((await usuarios.UpdateAsync(usuario)).Succeeded);
        return new Usuario_(login, email, id);
    }

    private async Task<HttpResponseMessage> PedirAsync(string usuarioOuEmail) =>
        await (await ClienteApi.AbrirAsync(factory)).PostAsync("/api/auth/esqueci-senha", new { usuarioOuEmail });

    private async Task<HttpResponseMessage> RedefinirAsync(string token, string novaSenha = NovaSenha) =>
        await (await ClienteApi.AbrirAsync(factory)).PostAsync("/api/auth/redefinir-senha", new { token, novaSenha });

    private async Task<string> TokenAsync(Usuario_ usuario, int quantidade = 1)
    {
        Assert.Equal(HttpStatusCode.NoContent, (await PedirAsync(usuario.Login)).StatusCode);
        return CaixaDeEmails.TokenDoLink(await factory.Emails.EsperarAsync(usuario.Email!, quantidade));
    }

    private async Task<int> EventosPublicadosAsync(Guid usuarioId)
    {
        await using var escopo = factory.Services.CreateAsyncScope();
        var banco = escopo.ServiceProvider.GetRequiredService<AcessoDbContext>();
        return await banco.Set<OutboxMensagem>().CountAsync(m => m.Tipo == TipoDoEvento && m.ChaveDeCorrelacao == $"usuario:{usuarioId}");
    }

    private static async Task<string[]> ErrosAsync(HttpResponseMessage resposta, string campo)
    {
        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        var problema = await resposta.Content.ReadFromJsonAsync<ProblemaDeValidacao>();
        Assert.True(problema!.Errors.TryGetValue(campo, out var erros), $"sem erro em {campo}");
        return erros;
    }

    private sealed record ProblemaDeValidacao(Dictionary<string, string[]> Errors);

    // ---- CA-2: esquema do cenário 1.10 ----

    public enum Situacao { AtivaComEmail, Inexistente, AtivoSemEmail, DesativadoComEmail }

    [Theory]
    [InlineData(Situacao.AtivaComEmail, true)]
    [InlineData(Situacao.Inexistente, false)]
    [InlineData(Situacao.AtivoSemEmail, false)]
    [InlineData(Situacao.DesativadoComEmail, false)]
    public async Task Cenario_1_10_responde_igual_e_so_envia_para_usuario_ativo_com_email(Situacao situacao, bool envia)
    {
        Usuario_? usuario = situacao switch
        {
            Situacao.AtivaComEmail => await NovoUsuarioAsync("ana"),
            Situacao.AtivoSemEmail => await NovoUsuarioAsync("bruno", comEmail: false),
            Situacao.DesativadoComEmail => await NovoUsuarioAsync("carlos", ativo: false),
            _ => null,
        };
        var informado = usuario?.Login ?? $"joana-{Guid.NewGuid():N}"[..20];

        var resposta = await PedirAsync(informado);

        // Mesma resposta (status e corpo) em todas as linhas: a tela mostra a mensagem fixa.
        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);
        Assert.Equal(string.Empty, await resposta.Content.ReadAsStringAsync());

        if (envia)
        {
            var email = await factory.Emails.EsperarAsync(usuario!.Email!);
            Assert.Contains("30 minutos", email.Texto);
            Assert.Contains($"{ImpaApiFactory.UrlDaAplicacao}/redefinir-senha#token=", email.Texto);
        }
        else if (usuario is not null)
        {
            Assert.Equal(0, await EventosPublicadosAsync(usuario.Id));
            if (usuario.Email is not null)
                Assert.Empty(factory.Emails.Para(usuario.Email));
        }
    }

    [Fact]
    public async Task Pedido_pelo_email_tambem_envia()
    {
        var ana = await NovoUsuarioAsync("anaemail");

        Assert.Equal(HttpStatusCode.NoContent, (await PedirAsync(ana.Email!.ToUpperInvariant())).StatusCode);

        await factory.Emails.EsperarAsync(ana.Email!);
    }

    [Fact]
    public async Task Email_de_dois_usuarios_ativos_manda_um_link_para_cada()
    {
        var primeiro = await NovoUsuarioAsync("dupla1");
        var segundo = await NovoUsuarioAsync("dupla2");
        var compartilhado = $"compartilhado-{Guid.NewGuid():N}@lojaexemplo.com.br";
        await using (var escopo = factory.Services.CreateAsyncScope())
        {
            var usuarios = escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
            foreach (var id in new[] { primeiro.Id, segundo.Id })
            {
                var u = (await usuarios.FindByIdAsync(id.ToString()))!;
                u.Email = compartilhado;
                Assert.True((await usuarios.UpdateAsync(u)).Succeeded);
            }
        }

        Assert.Equal(HttpStatusCode.NoContent, (await PedirAsync(compartilhado)).StatusCode);

        await factory.Emails.EsperarAsync(compartilhado, 2);
        var emails = factory.Emails.Para(compartilhado);
        Assert.Contains(emails, e => e.Texto.Contains(primeiro.Login));
        Assert.Contains(emails, e => e.Texto.Contains(segundo.Login));
        Assert.NotEqual(CaixaDeEmails.TokenDoLink(emails[0]), CaixaDeEmails.TokenDoLink(emails[1]));
    }

    [Fact]
    public async Task Pedido_vazio_e_recusado_pela_validacao()
    {
        Assert.Contains("Informe o usuário ou o e-mail", await ErrosAsync(await PedirAsync("  "), "UsuarioOuEmail"));
    }

    [Fact]
    public async Task Brevo_fora_do_ar_nao_afeta_a_resposta_e_o_despachante_tenta_de_novo()
    {
        var ana = await NovoUsuarioAsync("falha");
        factory.Emails.FalharPara(ana.Email!, vezes: 1);

        Assert.Equal(HttpStatusCode.NoContent, (await PedirAsync(ana.Login)).StatusCode);

        // A primeira tentativa falha; a próxima é agendada pelo despachante e entrega o e-mail.
        await using (var escopo = factory.Services.CreateAsyncScope())
        {
            var banco = escopo.ServiceProvider.GetRequiredService<AcessoDbContext>();
            var fim = DateTime.UtcNow.AddSeconds(20);
            while (!await banco.Set<OutboxMensagem>().AnyAsync(m =>
                       m.ChaveDeCorrelacao == $"usuario:{ana.Id}" && m.Tentativas >= 1))
            {
                if (DateTime.UtcNow > fim)
                    Assert.Fail("a falha do envio não foi registrada no Outbox");
                await Task.Delay(50);
            }
        }

        factory.Clock.Advance(TimeSpan.FromMinutes(5)); // vence o intervalo da nova tentativa
        factory.Services.GetRequiredService<SinalDoDespachante>().Acordar();
        await factory.Emails.EsperarAsync(ana.Email!);
    }

    // ---- CA-1, CA-3: cenário 1.9 ----

    [Fact]
    public async Task Cenario_1_9_link_redefine_a_senha_encerra_as_sessoes_e_nao_vale_de_novo()
    {
        var ana = await NovoUsuarioAsync("ana19");
        var sessao1 = await ClienteApi.AbrirAsync(factory);
        var sessao2 = await ClienteApi.AbrirAsync(factory);
        Assert.Equal(HttpStatusCode.NoContent, (await sessao1.EntrarAsync(ana.Login, Senha)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await sessao2.EntrarAsync(ana.Login, Senha)).StatusCode);

        var token = await TokenAsync(ana);

        Assert.Equal(HttpStatusCode.NoContent, (await RedefinirAsync(token)).StatusCode);

        // As sessões abertas de Ana são encerradas.
        Assert.Equal(HttpStatusCode.Unauthorized, (await sessao1.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await sessao2.GetAsync("/api/auth/me")).StatusCode);
        await using (var escopo = factory.Services.CreateAsyncScope())
        {
            var banco = escopo.ServiceProvider.GetRequiredService<AcessoDbContext>();
            Assert.All(await banco.Sessoes.Where(s => s.UsuarioId == ana.Id).ToListAsync(), s => Assert.NotNull(s.RevogadaEm));
        }

        // A senha nova vale; a antiga não.
        Assert.Equal(HttpStatusCode.NoContent, (await (await ClienteApi.AbrirAsync(factory)).EntrarAsync(ana.Login, NovaSenha)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await (await ClienteApi.AbrirAsync(factory)).EntrarAsync(ana.Login, Senha)).StatusCode);

        // O mesmo link usado de novo é recusado.
        Assert.Equal([MensagemLinkInvalido], await ErrosAsync(await RedefinirAsync(token, "outrasenha99"), "Token"));
    }

    [Fact]
    public async Task Redefinir_no_navegador_com_sessao_aberta_encerra_essa_sessao_tambem()
    {
        var ana = await NovoUsuarioAsync("mesmo");
        var navegador = await ClienteApi.AbrirAsync(factory);
        Assert.Equal(HttpStatusCode.NoContent, (await navegador.EntrarAsync(ana.Login, Senha)).StatusCode);
        var token = await TokenAsync(ana);

        var resposta = await navegador.PostAsync("/api/auth/redefinir-senha", new { token, novaSenha = NovaSenha });

        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await navegador.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await navegador.EntrarAsync(ana.Login, NovaSenha)).StatusCode); // token XSRF anônimo novo
    }

    [Fact]
    public async Task Link_vale_por_30_minutos()
    {
        var dentro = await NovoUsuarioAsync("dentro");
        var fora = await NovoUsuarioAsync("fora");
        var tokenDentro = await TokenAsync(dentro);
        var tokenFora = await TokenAsync(fora);

        factory.Clock.Advance(TimeSpan.FromMinutes(29));
        Assert.Equal(HttpStatusCode.NoContent, (await RedefinirAsync(tokenDentro)).StatusCode);

        factory.Clock.Advance(TimeSpan.FromMinutes(1).Add(TimeSpan.FromSeconds(1)));
        Assert.Equal([MensagemLinkInvalido], await ErrosAsync(await RedefinirAsync(tokenFora), "Token"));
    }

    [Fact]
    public async Task Redefinir_zera_a_contagem_de_bloqueio()
    {
        var ana = await NovoUsuarioAsync("bloq");
        var cliente = await ClienteApi.AbrirAsync(factory);
        for (var i = 0; i < 5; i++)
            await cliente.EntrarAsync(ana.Login, "errada-123");

        Assert.Equal(HttpStatusCode.NoContent, (await RedefinirAsync(await TokenAsync(ana))).StatusCode);

        await using (var escopo = factory.Services.CreateAsyncScope())
        {
            var usuario = (await escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>().FindByIdAsync(ana.Id.ToString()))!;
            Assert.Equal(0, usuario.AccessFailedCount);
            Assert.Null(usuario.LockoutEnd);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await (await ClienteApi.AbrirAsync(factory)).EntrarAsync(ana.Login, NovaSenha)).StatusCode);
    }

    [Fact]
    public async Task Senha_curta_e_recusada_e_o_link_continua_valendo()
    {
        var ana = await NovoUsuarioAsync("curta");
        var token = await TokenAsync(ana);

        Assert.NotEmpty(await ErrosAsync(await RedefinirAsync(token, "curta"), "NovaSenha"));

        Assert.Equal(HttpStatusCode.NoContent, (await RedefinirAsync(token)).StatusCode);
    }

    [Fact]
    public async Task Novo_pedido_nao_invalida_o_link_anterior_mas_usar_um_invalida_os_dois()
    {
        var ana = await NovoUsuarioAsync("dois");
        var primeiro = await TokenAsync(ana);
        var segundo = await TokenAsync(ana, quantidade: 2);

        Assert.Equal(HttpStatusCode.NoContent, (await RedefinirAsync(segundo)).StatusCode);

        Assert.Equal([MensagemLinkInvalido], await ErrosAsync(await RedefinirAsync(primeiro), "Token"));
    }

    [Fact]
    public async Task Usuario_desativado_depois_do_pedido_nao_redefine()
    {
        var ana = await NovoUsuarioAsync("desat");
        var token = await TokenAsync(ana);
        await using (var escopo = factory.Services.CreateAsyncScope())
        {
            var usuarios = escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
            var usuario = (await usuarios.FindByIdAsync(ana.Id.ToString()))!;
            usuario.Desativar();
            Assert.True((await usuarios.UpdateAsync(usuario)).Succeeded); // sem trocar o stamp: o Ativo é que barra
        }

        Assert.Equal([MensagemLinkInvalido], await ErrosAsync(await RedefinirAsync(token), "Token"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("nao-e-um-token")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task Token_forjado_ou_vazio_e_recusado(string token)
    {
        Assert.Equal([MensagemLinkInvalido], await ErrosAsync(await RedefinirAsync(token), "Token"));
    }

    [Fact]
    public async Task Redefinir_tira_a_troca_de_senha_obrigatoria()
    {
        var ana = await NovoUsuarioAsync("provis");
        await using (var escopo = factory.Services.CreateAsyncScope())
        {
            var usuarios = escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
            var usuario = (await usuarios.FindByIdAsync(ana.Id.ToString()))!;
            usuario.ExigirTrocaDeSenha();
            Assert.True((await usuarios.UpdateAsync(usuario)).Succeeded);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await RedefinirAsync(await TokenAsync(ana))).StatusCode);

        await using (var escopo = factory.Services.CreateAsyncScope())
        {
            var usuario = (await escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>().FindByIdAsync(ana.Id.ToString()))!;
            Assert.False(usuario.TrocaDeSenhaObrigatoria);
        }
    }
}
