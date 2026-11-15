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

    private async Task<int> EventosPublicadosAsync(Guid usuarioId) => (await PedidosDoUsuarioAsync(usuarioId)).Count;

    private async Task<List<OutboxMensagem>> PedidosDoUsuarioAsync(Guid usuarioId)
    {
        await using var escopo = factory.Services.CreateAsyncScope();
        var banco = escopo.ServiceProvider.GetRequiredService<AcessoDbContext>();
        return await banco.Set<OutboxMensagem>().AsNoTracking()
            .Where(m => m.Tipo == TipoDoEvento && m.ChaveDeCorrelacao == $"usuario:{usuarioId}")
            .OrderBy(m => m.OcorreuEm)
            .ToListAsync();
    }

    private async Task<int> TotalDePedidosAsync()
    {
        await using var escopo = factory.Services.CreateAsyncScope();
        return await escopo.ServiceProvider.GetRequiredService<AcessoDbContext>().Set<OutboxMensagem>().CountAsync(m => m.Tipo == TipoDoEvento);
    }

    /// <summary>Espera um pedido do usuário que satisfaça <paramref name="condicao"/> (o despachante é assíncrono).</summary>
    private async Task<OutboxMensagem> EsperarPedidoAsync(Guid usuarioId, Func<OutboxMensagem, bool> condicao, string descricao)
    {
        var fim = DateTime.UtcNow.AddSeconds(20);
        while (true)
        {
            if ((await PedidosDoUsuarioAsync(usuarioId)).FirstOrDefault(condicao) is { } pedido)
                return pedido;
            if (DateTime.UtcNow > fim)
                Assert.Fail($"o pedido de {usuarioId} não chegou a: {descricao}");
            await Task.Delay(50);
        }
    }

    /// <summary>Faz o despachante tentar de novo agora (vence o intervalo da nova tentativa no relógio controlado).</summary>
    private void AcordarDespachante(TimeSpan avanco)
    {
        factory.Clock.Advance(avanco);
        factory.Services.GetRequiredService<SinalDoDespachante>().Acordar();
    }

    private async Task AlterarAsync(Guid usuarioId, Action<Usuario> alteracao)
    {
        await using var escopo = factory.Services.CreateAsyncScope();
        var usuarios = escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        var usuario = (await usuarios.FindByIdAsync(usuarioId.ToString()))!;
        alteracao(usuario);
        Assert.True((await usuarios.UpdateAsync(usuario)).Succeeded);
    }

    private async Task<Usuario> BuscarAsync(Guid usuarioId)
    {
        await using var escopo = factory.Services.CreateAsyncScope();
        return (await escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>().FindByIdAsync(usuarioId.ToString()))!;
    }

    /// <summary>Gravação concorrente direto no banco, fora da requisição em andamento (outra conexão).</summary>
    private void GravarConcorrente(Guid usuarioId, string sets)
    {
        using var escopo = factory.Services.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<AcessoDbContext>();
#pragma warning disable EF1002 // sets é fixo no teste; o id vai por parâmetro
        banco.Database.ExecuteSqlRaw($"UPDATE acesso.usuarios SET {sets} WHERE \"Id\" = {{0}}", usuarioId);
#pragma warning restore EF1002
    }

    /// <summary>N do D2-a (padrão de <c>Acesso:RecuperacaoDeSenha:IntervaloMinimoEntrePedidosEmMinutos</c>).</summary>
    private static readonly TimeSpan IntervaloMinimo = TimeSpan.FromMinutes(5);

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
        var pedidosAntes = await TotalDePedidosAsync();

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
        else
        {
            // "Nenhum e-mail é enviado" = nenhum pedido gravado no Outbox durante a requisição: o e-mail só sai de lá,
            // então isso não depende do despachante assíncrono (R2 da revisão 1). Vale também para a "joana",
            // que não tem id para filtrar: os testes da classe rodam em sequência, então o total não pode mudar.
            Assert.Equal(pedidosAntes, await TotalDePedidosAsync());
            if (usuario is not null)
                Assert.Equal(0, await EventosPublicadosAsync(usuario.Id));
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

        // A primeira tentativa falha e fica registrada; a próxima é agendada pelo despachante e entrega o e-mail.
        var falhou = await EsperarPedidoAsync(ana.Id, m => m.Tentativas >= 1, "falha registrada");
        Assert.Equal(SituacaoDoEvento.Pendente, falhou.Situacao);
        Assert.Contains("Brevo fora do ar", falhou.UltimoErro);
        Assert.Empty(factory.Emails.Para(ana.Email!));

        AcordarDespachante(TimeSpan.FromMinutes(5)); // vence o intervalo da nova tentativa
        await factory.Emails.EsperarAsync(ana.Email!);
        await EsperarPedidoAsync(ana.Id, m => m.Situacao == SituacaoDoEvento.Entregue, "entregue");
    }

    // ---- R4: o que vai no Outbox e o que acontece entre o pedido e o envio ----

    [Fact]
    public async Task Outbox_leva_so_o_id_do_usuario_e_nunca_o_token()
    {
        var ana = await NovoUsuarioAsync("conteudo");
        var token = await TokenAsync(ana);

        var pedido = Assert.Single(await PedidosDoUsuarioAsync(ana.Id));
        using var json = System.Text.Json.JsonDocument.Parse(pedido.Conteudo);
        var campos = json.RootElement.EnumerateObject().Select(p => p.Name.ToLowerInvariant()).Order().ToArray();
        Assert.Equal(["chavedecorrelacao", "id", "ocorreuem", "usuarioid"], campos); // a chave é "usuario:{id}"
        Assert.DoesNotContain(token, pedido.Conteudo);
        Assert.DoesNotContain(ana.Email!, pedido.Conteudo, StringComparison.OrdinalIgnoreCase);
    }

    public enum MudancaAntesDoEnvio { Desativado, SemEmail }

    [Theory]
    [InlineData(MudancaAntesDoEnvio.Desativado)]
    [InlineData(MudancaAntesDoEnvio.SemEmail)]
    public async Task Usuario_que_deixou_de_ter_email_ativo_antes_do_envio_nao_recebe_nada(MudancaAntesDoEnvio mudanca)
    {
        var ana = await NovoUsuarioAsync("entre");
        factory.Emails.FalharPara(ana.Email!, vezes: 1); // segura o envio no Outbox, depois do pedido
        Assert.Equal(HttpStatusCode.NoContent, (await PedirAsync(ana.Login)).StatusCode);
        await EsperarPedidoAsync(ana.Id, m => m.Tentativas >= 1, "primeira tentativa");

        await AlterarAsync(ana.Id, u =>
        {
            if (mudanca == MudancaAntesDoEnvio.Desativado)
                u.Desativar();
            else
                u.Email = null;
        });
        AcordarDespachante(TimeSpan.FromMinutes(5));

        // PrepararEnvioAsync → null: o handler termina sem enviar e sem falhar (o evento fica processado).
        await EsperarPedidoAsync(ana.Id, m => m.Situacao == SituacaoDoEvento.Entregue, "processado");
        Assert.Empty(factory.Emails.Para(ana.Email!));
    }

    // ---- R16 (D2-a): intervalo mínimo entre pedidos e idade máxima do pedido ----

    [Fact]
    public async Task Segundo_pedido_antes_do_intervalo_minimo_nao_gera_evento_nem_email_e_depois_gera()
    {
        var ana = await NovoUsuarioAsync("interv");
        await TokenAsync(ana);

        factory.Clock.Advance(IntervaloMinimo - TimeSpan.FromSeconds(1));
        var resposta = await PedirAsync(ana.Login);
        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode); // mesma resposta
        Assert.Equal(string.Empty, await resposta.Content.ReadAsStringAsync());
        Assert.Equal(1, await EventosPublicadosAsync(ana.Id));

        factory.Clock.Advance(TimeSpan.FromSeconds(2));
        await TokenAsync(ana, quantidade: 2);
        Assert.Equal(2, await EventosPublicadosAsync(ana.Id));
    }

    [Fact]
    public async Task Pedido_com_falha_definitiva_nao_segura_um_pedido_novo()
    {
        // Só pendente ou entregue contam para o intervalo: um "com falha" não deixa o usuário sem link.
        var ana = await NovoUsuarioAsync("comfal");
        factory.Emails.FalharPara(ana.Email!, vezes: 1);
        Assert.Equal(HttpStatusCode.NoContent, (await PedirAsync(ana.Login)).StatusCode);
        await EsperarPedidoAsync(ana.Id, m => m.Tentativas >= 1, "primeira tentativa");
        await using (var escopo = factory.Services.CreateAsyncScope())
        {
            var banco = escopo.ServiceProvider.GetRequiredService<AcessoDbContext>();
            await banco.Set<OutboxMensagem>()
                .Where(m => m.ChaveDeCorrelacao == $"usuario:{ana.Id}")
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Situacao, SituacaoDoEvento.ComFalha));
        }

        Assert.Equal(HttpStatusCode.NoContent, (await PedirAsync(ana.Login)).StatusCode);

        Assert.Equal(2, await EventosPublicadosAsync(ana.Id));
    }

    [Fact]
    public async Task Pedido_mais_antigo_que_a_idade_maxima_e_descartado_sem_email()
    {
        var ana = await NovoUsuarioAsync("velho");
        factory.Emails.FalharPara(ana.Email!, vezes: 1);
        Assert.Equal(HttpStatusCode.NoContent, (await PedirAsync(ana.Login)).StatusCode);
        await EsperarPedidoAsync(ana.Id, m => m.Tentativas >= 1, "primeira tentativa");

        AcordarDespachante(TimeSpan.FromMinutes(61)); // a nova tentativa chega depois de 60 minutos do pedido

        await EsperarPedidoAsync(ana.Id, m => m.Situacao == SituacaoDoEvento.Entregue, "processado");
        Assert.Empty(factory.Emails.Para(ana.Email!));
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
    public async Task Redefinir_no_navegador_com_sessao_de_outro_usuario_nao_mexe_nessa_sessao()
    {
        // R6 (revisão 1): o operador logado no caixa onde Ana abre o link continua logado.
        var ana = await NovoUsuarioAsync("dona");
        var operador = await NovoUsuarioAsync("operador");
        var caixa = await ClienteApi.AbrirAsync(factory);
        Assert.Equal(HttpStatusCode.NoContent, (await caixa.EntrarAsync(operador.Login, Senha)).StatusCode);
        var token = await TokenAsync(ana);

        Assert.Equal(HttpStatusCode.NoContent, (await caixa.PostAsync("/api/auth/redefinir-senha", new { token, novaSenha = NovaSenha })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await caixa.GetAsync("/api/auth/me")).StatusCode);
        await using var escopo = factory.Services.CreateAsyncScope();
        var banco = escopo.ServiceProvider.GetRequiredService<AcessoDbContext>();
        Assert.Contains(await banco.Sessoes.Where(s => s.UsuarioId == operador.Id).ToListAsync(), s => s.RevogadaEm is null);
        Assert.Equal(HttpStatusCode.NoContent, (await caixa.SairAsync()).StatusCode); // o token XSRF emitido continua ligado ao operador
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
    public async Task Redefinir_zera_a_contagem_parcial_de_senhas_erradas()
    {
        // R1 (revisão 1): com menos de 5 erros a contagem não é zerada pelo próprio bloqueio — só a redefinição zera.
        var ana = await NovoUsuarioAsync("parcial");
        var cliente = await ClienteApi.AbrirAsync(factory);
        for (var i = 0; i < 3; i++)
            await cliente.EntrarAsync(ana.Login, "errada-123");
        Assert.Equal(3, (await BuscarAsync(ana.Id)).AccessFailedCount);

        Assert.Equal(HttpStatusCode.NoContent, (await RedefinirAsync(await TokenAsync(ana))).StatusCode);

        Assert.Equal(0, (await BuscarAsync(ana.Id)).AccessFailedCount);
    }

    [Fact]
    public async Task Redefinir_tira_o_bloqueio_por_tentativas()
    {
        var ana = await NovoUsuarioAsync("bloq");
        var cliente = await ClienteApi.AbrirAsync(factory);
        for (var i = 0; i < 5; i++)
            await cliente.EntrarAsync(ana.Login, "errada-123");
        Assert.NotNull((await BuscarAsync(ana.Id)).LockoutEnd); // bloqueada antes de redefinir

        Assert.Equal(HttpStatusCode.NoContent, (await RedefinirAsync(await TokenAsync(ana))).StatusCode);

        var depois = await BuscarAsync(ana.Id);
        Assert.Equal(0, depois.AccessFailedCount);
        Assert.Null(depois.LockoutEnd);
        Assert.Equal(HttpStatusCode.NoContent, (await (await ClienteApi.AbrirAsync(factory)).EntrarAsync(ana.Login, NovaSenha)).StatusCode);
    }

    // ---- R3, R7: gravações concorrentes ----

    [Fact]
    public async Task Mesmo_link_usado_duas_vezes_ao_mesmo_tempo_vale_so_uma()
    {
        var ana = await NovoUsuarioAsync("simult");
        var token = await TokenAsync(ana);

        var respostas = await Task.WhenAll(RedefinirAsync(token), RedefinirAsync(token, "outrasenha99"));

        Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.NoContent);
        var recusada = Assert.Single(respostas, r => r.StatusCode != HttpStatusCode.NoContent);
        Assert.Equal([MensagemLinkInvalido], await ErrosAsync(recusada, "Token"));
    }

    [Fact]
    public async Task Link_usado_por_outra_requisicao_no_meio_da_redefinicao_e_recusado()
    {
        // Versão determinística do teste acima: a outra redefinição grava (stamp novo) entre a leitura e a gravação.
        var ana = await NovoUsuarioAsync("meio");
        var token = await TokenAsync(ana);
        factory.Hasher.AntesDoProximoHash(ana.Id, () => GravarConcorrente(ana.Id,
            "\"SecurityStamp\" = gen_random_uuid()::text, \"ConcurrencyStamp\" = gen_random_uuid()::text"));

        Assert.Equal([MensagemLinkInvalido], await ErrosAsync(await RedefinirAsync(token), "Token"));
    }

    [Fact]
    public async Task Senha_errada_no_login_no_meio_da_redefinicao_nao_recusa_um_link_valido()
    {
        // R7 (revisão 1): o login errado troca só o ConcurrencyStamp (SessaoService.RegistrarFalhaAsync); o link
        // continua valendo e a redefinição tenta de novo.
        var ana = await NovoUsuarioAsync("concor");
        var token = await TokenAsync(ana);
        factory.Hasher.AntesDoProximoHash(ana.Id, () => GravarConcorrente(ana.Id,
            "\"AccessFailedCount\" = \"AccessFailedCount\" + 1, \"ConcurrencyStamp\" = gen_random_uuid()::text"));

        Assert.Equal(HttpStatusCode.NoContent, (await RedefinirAsync(token)).StatusCode);

        Assert.Equal(0, (await BuscarAsync(ana.Id)).AccessFailedCount);
        Assert.Equal(HttpStatusCode.NoContent, (await (await ClienteApi.AbrirAsync(factory)).EntrarAsync(ana.Login, NovaSenha)).StatusCode);
    }

    // ---- R10: e-mail trocado depois do envio ----

    public enum MudancaDoEmail { Trocado, Removido }

    [Theory]
    [InlineData(MudancaDoEmail.Trocado)]
    [InlineData(MudancaDoEmail.Removido)]
    public async Task Link_enviado_a_um_email_que_nao_e_mais_do_usuario_e_recusado(MudancaDoEmail mudanca)
    {
        var ana = await NovoUsuarioAsync("trocaem");
        var token = await TokenAsync(ana);
        await AlterarAsync(ana.Id, u => u.Email = mudanca == MudancaDoEmail.Trocado ? $"nova-{u.Email}" : null);

        Assert.Equal([MensagemLinkInvalido], await ErrosAsync(await RedefinirAsync(token), "Token"));
    }

    [Fact]
    public async Task Email_so_com_maiusculas_diferentes_continua_valendo()
    {
        var ana = await NovoUsuarioAsync("caixa");
        var token = await TokenAsync(ana);
        await AlterarAsync(ana.Id, u => u.Email = u.Email!.ToUpperInvariant());

        Assert.Equal(HttpStatusCode.NoContent, (await RedefinirAsync(token)).StatusCode);
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
        factory.Clock.Advance(IntervaloMinimo + TimeSpan.FromSeconds(1)); // antes disso, o segundo pedido seria ignorado (D2-a)
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
