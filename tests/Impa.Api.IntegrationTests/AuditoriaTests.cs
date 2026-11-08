using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Impa.Modules.Acesso.Application.Perfis;
using Impa.Modules.Acesso.Contracts;
using Impa.Modules.Configuracao.Contracts.Auditoria;
using Impa.Modules.Configuracao.Infrastructure.Persistence;
using Impa.Shared.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Impa.Api.IntegrationTests;

/// <summary>
/// Story 4.4, T5 — auditoria das operações sensíveis de ponta a ponta: o evento publicado no Outbox é gravado uma vez
/// (Inbox) e consultado em <c>GET /api/auditoria</c> com <c>auditoria.ver</c> (CA-1 a CA-5). Os módulos que publicam
/// de verdade (3.3, 3.5, épico 8) ainda não existem: os eventos de teste saem do Outbox do Configuração.
/// </summary>
public class AuditoriaTests(ImpaApiFactory factory) : IClassFixture<ImpaApiFactory>
{
    private sealed record CampoResposta(string Campo, string? Antes, string? Depois, bool Omitido);

    private sealed record ItemResposta(
        Guid Id, DateTimeOffset OcorreuEm, Guid UsuarioId, string UsuarioNome, string Modulo, string Entidade,
        string EntidadeId, string Operacao, List<CampoResposta> Campos);

    private sealed record PaginaResposta(List<ItemResposta> Itens, int Pagina, int TamanhoDaPagina, int Total);

    private async Task<ClienteApi> AdminAsync()
    {
        var cliente = await ClienteApi.AbrirAsync(factory);
        Assert.Equal(HttpStatusCode.NoContent,
            (await cliente.EntrarAsync(ImpaApiFactory.AdminUsuario, ImpaApiFactory.AdminSenha)).StatusCode);
        return cliente;
    }

    private static async Task<PaginaResposta> ConsultarAsync(ClienteApi cliente, string consulta)
    {
        var resposta = await cliente.GetAsync($"/api/auditoria{consulta}");
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        return (await resposta.Content.ReadFromJsonAsync<PaginaResposta>())!;
    }

    private static string Iso(DateTimeOffset data) => Uri.EscapeDataString(data.ToString("O"));

    /// <summary>O módulo de origem publica no próprio Outbox, na transação da alteração (aqui, o do Configuração).</summary>
    private async Task<OperacaoSensivelAuditada> PublicarAsync(OperacaoSensivelAuditada evento)
    {
        await using var escopo = factory.Services.CreateAsyncScope();
        var contexto = escopo.ServiceProvider.GetRequiredService<ConfiguracaoDbContext>();
        escopo.ServiceProvider.GetRequiredService<IEventPublisher<ConfiguracaoDbContext>>().Publicar(evento);
        await contexto.SaveChangesAsync();
        await EntregueAsync(evento.Id);
        return evento;
    }

    private async Task<T> NoBancoAsync<T>(Func<ConfiguracaoDbContext, Task<T>> consulta)
    {
        await using var escopo = factory.Services.CreateAsyncScope();
        return await consulta(escopo.ServiceProvider.GetRequiredService<ConfiguracaoDbContext>());
    }

    private async Task EntregueAsync(Guid eventoId)
    {
        var fim = DateTime.UtcNow.AddSeconds(20);
        while (!await NoBancoAsync(c => c.Set<OutboxMensagem>()
                   .AnyAsync(m => m.Id == eventoId && m.Situacao == SituacaoDoEvento.Entregue && m.EntregueEm != null)))
        {
            if (DateTime.UtcNow > fim)
                Assert.Fail("evento de auditoria não entregue ao Configuração");
            await Task.Delay(50);
        }
    }

    private static OperacaoSensivelAuditada Evento(
        Guid usuarioId, string usuarioNome, string modulo, string entidade, string operacao, DateTimeOffset ocorreuEm,
        params CampoAlterado[] campos) =>
        new(usuarioId, usuarioNome, modulo, entidade, Guid.NewGuid().ToString(), operacao, campos) { OcorreuEm = ocorreuEm };

    [Fact]
    public async Task Evento_entregue_duas_vezes_e_gravado_uma_vez_so()
    {
        var evento = await PublicarAsync(Evento(Guid.NewGuid(), "Duplicada", "Catalogo", "Item", "custo.alterado",
            DateTimeOffset.UtcNow, new CampoAlterado("Custo", "1,00", "2,00")));
        Assert.Equal(1, await NoBancoAsync(c => c.Auditoria.CountAsync(r => r.Id == evento.Id)));

        // Reentrega (ex.: a API caiu antes de confirmar a entrega): o evento volta a pendente no Outbox.
        await NoBancoAsync(c => c.Database.ExecuteSqlAsync(
            $"UPDATE configuracao.outbox SET situacao = 'Pendente', entregue_em = NULL WHERE id = {evento.Id}"));
        factory.Services.GetRequiredService<SinalDoDespachante>().Acordar();
        await EntregueAsync(evento.Id);

        Assert.Equal(1, await NoBancoAsync(c => c.Auditoria.CountAsync(r => r.Id == evento.Id)));
        Assert.Equal(1, await NoBancoAsync(c => c.Set<InboxMensagem>().CountAsync(i => i.EventoId == evento.Id)));
    }

    [Fact]
    public async Task Consulta_por_periodo_e_por_usuario_mostra_usuario_data_e_antes_e_depois()
    {
        var dia = new DateTimeOffset(2021, 3, 10, 0, 0, 0, TimeSpan.Zero);
        var gabi = Guid.NewGuid();
        var hugo = Guid.NewGuid();
        var primeira = await PublicarAsync(Evento(gabi, "Gabriela Prado", "Catalogo", "Item", "preco.alterado",
            dia.AddHours(9), new CampoAlterado("PrecoDeVenda", "5,90", "6,50")));
        var segunda = await PublicarAsync(Evento(gabi, "Gabriela Prado", "Estoque", "Saldo", "estoque.ajustado",
            dia.AddHours(15), new CampoAlterado("Quantidade", "10", "8")));
        await PublicarAsync(Evento(hugo, "Hugo Prado", "Catalogo", "Item", "preco.alterado",
            dia.AddHours(10), new CampoAlterado("PrecoDeVenda", "2,00", "2,20")));
        await PublicarAsync(Evento(gabi, "Gabriela Prado", "Catalogo", "Item", "preco.alterado",
            dia.AddDays(1).AddHours(9), new CampoAlterado("PrecoDeVenda", "6,50", "7,00"))); // fora do período
        var admin = await AdminAsync();

        var doDia = await ConsultarAsync(admin, $"?de={Iso(dia)}&ate={Iso(dia.AddDays(1))}");
        Assert.Equal(3, doDia.Total);

        var daGabi = await ConsultarAsync(admin, $"?de={Iso(dia)}&ate={Iso(dia.AddDays(1))}&usuario=gabriela");
        Assert.Equal(2, daGabi.Total);
        Assert.Equal([segunda.Id, primeira.Id], daGabi.Itens.Select(i => i.Id)); // mais recentes primeiro
        var preco = daGabi.Itens[1];
        Assert.Equal((gabi, "Gabriela Prado"), (preco.UsuarioId, preco.UsuarioNome));
        Assert.Equal(dia.AddHours(9), preco.OcorreuEm);
        Assert.Equal(("Catalogo", "Item", primeira.EntidadeId, "preco.alterado"),
            (preco.Modulo, preco.Entidade, preco.EntidadeId, preco.Operacao));
        Assert.Equal(new CampoResposta("PrecoDeVenda", "5,90", "6,50", false), Assert.Single(preco.Campos));

        var doModulo = await ConsultarAsync(admin, $"?de={Iso(dia)}&ate={Iso(dia.AddDays(1))}&modulo=estoque&entidade=saldo");
        Assert.Equal(segunda.Id, Assert.Single(doModulo.Itens).Id);

        var paginada = await ConsultarAsync(admin, $"?de={Iso(dia)}&ate={Iso(dia.AddDays(1))}&pagina=2&tamanho=2");
        Assert.Equal((2, 2, 3), (paginada.Pagina, paginada.TamanhoDaPagina, paginada.Total));
        Assert.Equal(primeira.Id, Assert.Single(paginada.Itens).Id);
    }

    [Fact]
    public async Task Eventos_das_tres_regras_do_CA_3_ficam_consultaveis()
    {
        var dia = new DateTimeOffset(2021, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var usuario = await factory.IdDoUsuarioAsync(ImpaApiFactory.AdminUsuario);
        // RN-3.8 (story 3.3): alteração manual de custo
        var custo = await PublicarAsync(Evento(usuario, ImpaApiFactory.AdminNome, "Catalogo", "Item", "custo.alterado",
            dia.AddHours(8), new CampoAlterado("Custo", "10,00", "12,50")));
        // RN-4.4 (story 3.5): alteração do perfil de tributação
        var perfil = await PublicarAsync(Evento(usuario, ImpaApiFactory.AdminNome, "Fiscal", "PerfilDeTributacao",
            "perfil-de-tributacao.alterado", dia.AddHours(9),
            new CampoAlterado("Cfop", "5102", "5405"), new CampoAlterado("CstCsosn", "102", "500")));
        // RN-15.6 (épico 8): configuração fiscal, sem guardar senha nem CSC
        var fiscal = await PublicarAsync(Evento(usuario, ImpaApiFactory.AdminNome, "Fiscal", "ConfiguracaoFiscal",
            "configuracao-fiscal.alterada", dia.AddHours(10),
            new CampoAlterado("Ambiente", "Homologação", "Produção"),
            new CampoAlterado("Csc", "CSC-ANTIGO-123", "CSC-NOVO-456"),
            new CampoAlterado("SenhaDoCertificado", "senha-antiga", "senha-nova")));
        var admin = await AdminAsync();

        var itens = (await ConsultarAsync(admin, $"?de={Iso(dia)}&ate={Iso(dia.AddDays(1))}")).Itens;

        Assert.Equal([fiscal.Id, perfil.Id, custo.Id], itens.Select(i => i.Id));
        Assert.Equal(new CampoResposta("Custo", "10,00", "12,50", false), Assert.Single(itens[2].Campos));
        Assert.Equal(["Cfop: 5102 → 5405", "CstCsosn: 102 → 500"], itens[1].Campos.Select(c => $"{c.Campo}: {c.Antes} → {c.Depois}"));
        Assert.Equal(
            [
                new CampoResposta("Ambiente", "Homologação", "Produção", false),
                new CampoResposta("Csc", null, null, true),
                new CampoResposta("SenhaDoCertificado", null, null, true),
            ],
            itens[0].Campos);
        Assert.All(itens, i => Assert.Equal(ImpaApiFactory.AdminNome, i.UsuarioNome));
    }

    [Fact]
    public async Task Campo_sensivel_e_recusado_nem_vai_para_o_banco()
    {
        var evento = await PublicarAsync(Evento(Guid.NewGuid(), "Iara", "Pessoas", "Cliente", "cliente.alterado",
            DateTimeOffset.UtcNow,
            new CampoAlterado("Nome", "Iara S.", "Iara Souza"),
            new CampoAlterado("Cpf", "111.444.777-35", "529.982.247-25"),
            new CampoAlterado("Telefone", "31 99999-0000", "31 98888-0000"),
            new CampoAlterado("Email", "iara@antigo.com", "iara@novo.com"),
            new CampoAlterado("Endereco", "Rua A, 1", "Rua B, 2"),
            new CampoAlterado("Observacao", "x", "y", Sensivel: true)));

        var gravado = await NoBancoAsync(c => c.Database
            .SqlQuery<string>($"SELECT campos::text AS \"Value\" FROM configuracao.auditoria WHERE \"Id\" = {evento.Id}")
            .SingleAsync());

        foreach (var valor in new[] { "111.444.777-35", "529.982.247-25", "99999-0000", "98888-0000", "iara@", "Rua A", "Rua B", "\"x\"", "\"y\"" })
            Assert.DoesNotContain(valor, gravado);
        Assert.Contains("Iara Souza", gravado);

        var item = Assert.Single((await ConsultarAsync(await AdminAsync(), "?usuario=iara")).Itens);
        Assert.Equal(["Cpf", "Telefone", "Email", "Endereco", "Observacao"], item.Campos.Where(c => c.Omitido).Select(c => c.Campo));
        Assert.All(item.Campos.Where(c => c.Omitido), c => Assert.Equal((null, null), (c.Antes, c.Depois)));
    }

    [Fact]
    public async Task Operador_de_caixa_recebe_403_e_sem_sessao_401()
    {
        await factory.CriarUsuarioAsync("ana", "balcao2026", "Ana", PerfisPadrao.OperadorDeCaixa.Nome);
        var ana = await ClienteApi.AbrirAsync(factory);
        Assert.Equal(HttpStatusCode.NoContent, (await ana.EntrarAsync("ana", "balcao2026")).StatusCode);
        var anonimo = await ClienteApi.AbrirAsync(factory);

        Assert.Equal(HttpStatusCode.Forbidden, (await ana.GetAsync("/api/auditoria")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/auditoria")).StatusCode);
    }

    [Fact]
    public async Task Permissao_nova_esta_no_catalogo_do_administrador_e_no_auth_me()
    {
        var admin = await AdminAsync();

        var me = JsonDocument.Parse(await (await admin.GetAsync("/api/auth/me")).Content.ReadAsStringAsync()).RootElement;

        Assert.Contains(Permissoes.AuditoriaVer, me.GetProperty("permissoes").EnumerateArray().Select(p => p.GetString()));
        Assert.DoesNotContain(Permissoes.AuditoriaVer, PerfisPadrao.OperadorDeCaixa.Permissoes);
    }

    [Fact]
    public async Task Filtros_invalidos_respondem_400()
    {
        var admin = await AdminAsync();
        var agora = DateTimeOffset.UtcNow;

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/auditoria?tamanho=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/auditoria?pagina=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.GetAsync($"/api/auditoria?de={Iso(agora)}&ate={Iso(agora.AddDays(-1))}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/auditoria?usuario=100%25_\\")).StatusCode);
    }
}
