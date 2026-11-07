using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Impa.Modules.Acesso.Application.Perfis;
using Impa.Modules.Acesso.Application.Usuarios;
using Impa.Modules.Acesso.Contracts;
using Impa.Modules.Acesso.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Impa.Api.IntegrationTests;

/// <summary>Story 1-4 — gestão de usuários e senha provisória: cenário 1.7 (CA-1) e RN-1.8 (CA-2).</summary>
public class UsuariosTests(ImpaApiFactory factory) : IClassFixture<ImpaApiFactory>
{
    private const string CodigoTrocaObrigatoria = "troca_de_senha_obrigatoria";

    private sealed record ErroResposta(string Mensagem, string? Codigo);

    private async Task<ClienteApi> EntrarAsync(string usuario, string senha)
    {
        var cliente = await ClienteApi.AbrirAsync(factory);
        Assert.Equal(HttpStatusCode.NoContent, (await cliente.EntrarAsync(usuario, senha)).StatusCode);
        return cliente;
    }

    private Task<ClienteApi> AdminAsync() => EntrarAsync(ImpaApiFactory.AdminUsuario, ImpaApiFactory.AdminSenha);

    private async Task<Guid> PerfilIdAsync(ClienteApi admin, string nome) =>
        (await (await admin.GetAsync("/api/perfis")).Content.ReadFromJsonAsync<List<PerfilDto>>())!.Single(p => p.Nome == nome).Id;

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage resposta) =>
        JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;

    private async Task<(Guid Id, string Senha)> CriarAsync(ClienteApi admin, string login, string nome, string? senha = null)
    {
        var perfilId = await PerfilIdAsync(admin, PerfisPadrao.OperadorDeCaixa.Nome);
        var resposta = await admin.PostAsync("/api/usuarios", new { usuario = login, nome, email = (string?)null, perfilId, senha });
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var criado = (await resposta.Content.ReadFromJsonAsync<SenhaProvisoriaDto>())!;
        return (criado.Id, criado.SenhaProvisoria);
    }

    // ---- Cenário 1.7 (CA-1, CA-2), de ponta a ponta ----

    [Fact]
    public async Task Cenario_1_7_senha_provisoria_obriga_a_troca_no_proximo_acesso()
    {
        var admin = await AdminAsync();
        var (anaId, inicial) = await CriarAsync(admin, "ana17", "Ana");

        // Primeiro acesso com a senha inicial: troca e segue.
        var primeira = await EntrarAsync("ana17", inicial);
        Assert.Equal(HttpStatusCode.NoContent,
            (await primeira.PostAsync("/api/auth/trocar-senha", new { senhaAtual = inicial, novaSenha = "balcao-2026" })).StatusCode);

        // Dado que o administrador redefiniu a senha de Ana para a senha provisória "temp-4821"
        var redefinida = await admin.PostAsync($"/api/usuarios/{anaId}/redefinir-senha", new { senha = "temp-4821" });
        Assert.Equal(HttpStatusCode.OK, redefinida.StatusCode);
        Assert.Equal("temp-4821", (await redefinida.Content.ReadFromJsonAsync<SenhaProvisoriaDto>())!.SenhaProvisoria);

        // Quando Ana entra com a senha provisória
        var ana = await EntrarAsync("ana17", "temp-4821");

        // Então Ana precisa definir uma nova senha antes de acessar qualquer função
        var me = await JsonAsync(await ana.GetAsync("/api/auth/me"));
        Assert.True(me.GetProperty("trocaDeSenhaObrigatoria").GetBoolean());

        var bloqueada = await ana.PostAsync(EndpointsDeExemplo.Pdv);
        Assert.Equal(HttpStatusCode.Forbidden, bloqueada.StatusCode);
        Assert.Equal(CodigoTrocaObrigatoria, (await bloqueada.Content.ReadFromJsonAsync<ErroResposta>())!.Codigo);
        Assert.Equal(CodigoTrocaObrigatoria,
            (await (await ana.GetAsync(EndpointsDeExemplo.Item)).Content.ReadFromJsonAsync<ErroResposta>())!.Codigo);

        // Define a nova senha: o acesso volta ao normal, sem novo login.
        Assert.Equal(HttpStatusCode.NoContent,
            (await ana.PostAsync("/api/auth/trocar-senha", new { senhaAtual = "temp-4821", novaSenha = "nova-senha-2026" })).StatusCode);
        Assert.False((await JsonAsync(await ana.GetAsync("/api/auth/me"))).GetProperty("trocaDeSenhaObrigatoria").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, (await ana.PostAsync(EndpointsDeExemplo.Pdv)).StatusCode);

        // A provisória não vale mais; a nova vale.
        var depois = await ClienteApi.AbrirAsync(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await depois.EntrarAsync("ana17", "temp-4821")).StatusCode);
        await EntrarAsync("ana17", "nova-senha-2026");
    }

    [Fact]
    public async Task Novo_usuario_entra_com_a_senha_inicial_provisoria_e_precisa_troca_la()
    {
        var admin = await AdminAsync();
        var (_, senha) = await CriarAsync(admin, "bruno", "Bruno");

        Assert.Matches("^temp-[0-9]{4}$", senha); // gerada pelo sistema
        var bruno = await EntrarAsync("bruno", senha);
        Assert.True((await JsonAsync(await bruno.GetAsync("/api/auth/me"))).GetProperty("trocaDeSenhaObrigatoria").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await bruno.GetAsync(EndpointsDeExemplo.Item)).StatusCode);
    }

    [Fact]
    public async Task Na_troca_obrigatoria_o_usuario_ainda_consegue_sair()
    {
        var admin = await AdminAsync();
        var (_, senha) = await CriarAsync(admin, "carla", "Carla");
        var carla = await EntrarAsync("carla", senha);

        Assert.Equal(HttpStatusCode.NoContent, (await carla.SairAsync()).StatusCode);
    }

    [Fact]
    public async Task Troca_recusa_senha_atual_errada_e_nova_senha_curta_ou_igual()
    {
        var admin = await AdminAsync();
        var (_, senha) = await CriarAsync(admin, "davi", "Davi");
        var davi = await EntrarAsync("davi", senha);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await davi.PostAsync("/api/auth/trocar-senha", new { senhaAtual = "errada-123", novaSenha = "nova-senha-2026" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await davi.PostAsync("/api/auth/trocar-senha", new { senhaAtual = senha, novaSenha = "curta" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await davi.PostAsync("/api/auth/trocar-senha", new { senhaAtual = senha, novaSenha = senha })).StatusCode);
        Assert.True((await JsonAsync(await davi.GetAsync("/api/auth/me"))).GetProperty("trocaDeSenhaObrigatoria").GetBoolean());
    }

    // ---- T1: gestão de usuários ----

    [Fact]
    public async Task Lista_criar_editar_desativar_e_reativar()
    {
        var admin = await AdminAsync();
        var (id, _) = await CriarAsync(admin, "edu", "Edu");

        var lista = (await (await admin.GetAsync("/api/usuarios")).Content.ReadFromJsonAsync<List<UsuarioDto>>())!;
        var edu = Assert.Single(lista, u => u.Id == id);
        Assert.Equal(("edu", "Edu", PerfisPadrao.OperadorDeCaixa.Nome, true, true),
            (edu.Usuario, edu.Nome, edu.Perfil, edu.Ativo, edu.TrocaDeSenhaObrigatoria));
        Assert.Contains(lista, u => u.Usuario == ImpaApiFactory.AdminUsuario && u.Perfil == PerfisPadrao.Administrador.Nome);

        var gerente = (await (await admin.PostAsync("/api/perfis", new { nome = "Gerente", permissoes = Array.Empty<string>() }))
            .Content.ReadFromJsonAsync<PerfilDto>())!;
        var alterado = await admin.PutAsync($"/api/usuarios/{id}", new { nome = "Eduardo", email = "edu@impacto.com.br", perfilId = gerente.Id });
        Assert.Equal(HttpStatusCode.OK, alterado.StatusCode);
        var depois = (await alterado.Content.ReadFromJsonAsync<UsuarioDto>())!;
        Assert.Equal(("Eduardo", "edu@impacto.com.br", "Gerente"), (depois.Nome, depois.Email, depois.Perfil));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/usuarios/{id}/desativar")).StatusCode);
        Assert.False((await (await admin.GetAsync("/api/usuarios")).Content.ReadFromJsonAsync<List<UsuarioDto>>())!.Single(u => u.Id == id).Ativo);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/usuarios/{id}/reativar")).StatusCode);
        Assert.True((await (await admin.GetAsync("/api/usuarios")).Content.ReadFromJsonAsync<List<UsuarioDto>>())!.Single(u => u.Id == id).Ativo);
    }

    [Fact]
    public async Task Login_repetido_responde_409_e_dados_invalidos_400()
    {
        var admin = await AdminAsync();
        await CriarAsync(admin, "fabi", "Fabi");
        var perfilId = await PerfilIdAsync(admin, PerfisPadrao.OperadorDeCaixa.Nome);

        var repetido = await admin.PostAsync("/api/usuarios", new { usuario = "FABI", nome = "Outra", perfilId });
        Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
        Assert.Equal(MensagensUsuarios.LoginEmUso, (await repetido.Content.ReadFromJsonAsync<ErroResposta>())!.Mensagem);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsync("/api/usuarios", new { usuario = "gabi", nome = "", perfilId })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsync("/api/usuarios", new { usuario = "gabi", nome = "Gabi", email = "sem-arroba", perfilId })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsync("/api/usuarios", new { usuario = "gabi", nome = "Gabi", perfilId, senha = "curta" })).StatusCode);
    }

    [Fact]
    public async Task Operador_de_caixa_nao_gerencia_usuarios()
    {
        var admin = await AdminAsync();
        var (id, senha) = await CriarAsync(admin, "hugo", "Hugo");
        var hugo = await EntrarAsync("hugo", senha);
        await hugo.PostAsync("/api/auth/trocar-senha", new { senhaAtual = senha, novaSenha = "hugo-balcao-2026" });

        Assert.Equal(HttpStatusCode.Forbidden, (await hugo.GetAsync("/api/usuarios")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await hugo.PostAsync($"/api/usuarios/{id}/redefinir-senha")).StatusCode);
    }

    [Fact]
    public async Task Rebaixar_o_unico_administrador_responde_409()
    {
        var admin = await AdminAsync();
        var adminId = await factory.IdDoUsuarioAsync(ImpaApiFactory.AdminUsuario);
        var operador = await PerfilIdAsync(admin, PerfisPadrao.OperadorDeCaixa.Nome);

        // Nenhum teste desta classe cria outro administrador: o admin do seed é o único.
        var resposta = await admin.PutAsync($"/api/usuarios/{adminId}", new { nome = ImpaApiFactory.AdminNome, perfilId = operador });

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal(MensagensUsuarios.UltimoAdministrador, (await resposta.Content.ReadFromJsonAsync<ErroResposta>())!.Mensagem);
    }

    [Fact]
    public async Task Redefinir_sem_informar_gera_a_senha_provisoria()
    {
        var admin = await AdminAsync();
        var (id, _) = await CriarAsync(admin, "iris", "Iris", senha: "inicial-iris");

        var resposta = await admin.PostAsync($"/api/usuarios/{id}/redefinir-senha");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var senha = (await resposta.Content.ReadFromJsonAsync<SenhaProvisoriaDto>())!.SenhaProvisoria;
        Assert.Matches("^temp-[0-9]{4}$", senha);
        await EntrarAsync("iris", senha);
    }

    [Fact]
    public async Task Usuario_inexistente_responde_404()
    {
        var admin = await AdminAsync();
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"/api/usuarios/{id}/redefinir-senha")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"/api/usuarios/{id}/reativar")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.PutAsync($"/api/usuarios/{id}", new { nome = "X", perfilId = await PerfilIdAsync(admin, "Administrador") })).StatusCode);
    }

    // ---- Correções da revisão 1 ----

    [Fact]
    public async Task R2_login_tomado_entre_a_conferencia_e_a_criacao_nao_lanca_e_nao_duplica()
    {
        var admin = await AdminAsync();
        await CriarAsync(admin, "jota", "Jota");
        var perfilId = await PerfilIdAsync(admin, PerfisPadrao.OperadorDeCaixa.Nome);

        // Simula a corrida: o handler já conferiu que o login estava livre, mas outro cadastro chegou antes.
        await using var escopo = factory.Services.CreateAsyncScope();
        var cadastro = escopo.ServiceProvider.GetRequiredService<ICadastroUsuarios>();
        var id = await cadastro.CriarAsync(new NovoUsuario("JOTA", "Outro Jota", null, perfilId), "temp-1234", default);

        Assert.Null(id);
        var lista = (await (await admin.GetAsync("/api/usuarios")).Content.ReadFromJsonAsync<List<UsuarioDto>>())!;
        Assert.Single(lista, u => string.Equals(u.Usuario, "jota", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task R3_perfil_com_usuarios_gerenciar_e_sem_perfis_gerenciar_lista_usuarios_e_perfis()
    {
        var admin = await AdminAsync();
        var supervisor = await admin.PostAsync("/api/perfis",
            new { nome = "Supervisor de usuários", permissoes = new[] { Permissoes.UsuariosGerenciar } });
        Assert.Equal(HttpStatusCode.Created, supervisor.StatusCode);
        await factory.CriarUsuarioAsync("kika", "kika-senha-2026", "Kika", "Supervisor de usuários");
        var kika = await EntrarAsync("kika", "kika-senha-2026");

        Assert.Equal(HttpStatusCode.Forbidden, (await kika.GetAsync("/api/perfis")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await kika.GetAsync("/api/usuarios")).StatusCode);

        var resposta = await kika.GetAsync("/api/usuarios/perfis");
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var perfis = (await JsonAsync(resposta)).EnumerateArray().ToList();
        var administrador = Assert.Single(perfis, p => p.GetProperty("nome").GetString() == PerfisPadrao.Administrador.Nome);
        Assert.True(administrador.GetProperty("protegido").GetBoolean());
        Assert.Contains(perfis, p => p.GetProperty("nome").GetString() == PerfisPadrao.OperadorDeCaixa.Nome
                                     && !p.GetProperty("protegido").GetBoolean());
        // Só id, nome e protegido: nada das permissões nem da contagem de usuários.
        Assert.All(perfis, p => Assert.Equal(["id", "nome", "protegido"],
            p.EnumerateObject().Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal)));
    }

    [Fact]
    public async Task R3_perfis_da_tela_usuarios_exigem_usuarios_gerenciar()
    {
        var admin = await AdminAsync();
        var (_, senha) = await CriarAsync(admin, "lia", "Lia");
        var lia = await EntrarAsync("lia", senha);
        await lia.PostAsync("/api/auth/trocar-senha", new { senhaAtual = senha, novaSenha = "lia-balcao-2026" });

        Assert.Equal(HttpStatusCode.Forbidden, (await lia.GetAsync("/api/usuarios/perfis")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await (await ClienteApi.AbrirAsync(factory)).GetAsync("/api/usuarios/perfis")).StatusCode);
    }

    [Fact]
    public async Task R4_redefinir_a_senha_desbloqueia_o_usuario_bloqueado_por_tentativas()
    {
        var admin = await AdminAsync();
        var (id, _) = await CriarAsync(admin, "mila", "Mila", senha: "inicial-mila");

        // RN-1.2: cinco senhas erradas bloqueiam por 15 minutos.
        var cliente = await ClienteApi.AbrirAsync(factory);
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.EntrarAsync("mila", "errada-123")).StatusCode);
        Assert.Equal("bloqueado",
            (await (await cliente.EntrarAsync("mila", "inicial-mila")).Content.ReadFromJsonAsync<ErroResposta>())!.Codigo);

        var redefinida = await admin.PostAsync($"/api/usuarios/{id}/redefinir-senha", new { senha = "temp-5150" });
        Assert.Equal(HttpStatusCode.OK, redefinida.StatusCode);

        // Contagem de falhas e fim do bloqueio zerados já na redefinição (antes de qualquer login).
        await using (var escopo = factory.Services.CreateAsyncScope())
        {
            var usuario = (await escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>().FindByIdAsync(id.ToString()))!;
            Assert.Equal((0, (DateTimeOffset?)null), (usuario.AccessFailedCount, usuario.LockoutEnd));
        }

        // Entra com a provisória e cai na troca obrigatória (cenário 1.7).
        var mila = await EntrarAsync("mila", "temp-5150");
        Assert.True((await JsonAsync(await mila.GetAsync("/api/auth/me"))).GetProperty("trocaDeSenhaObrigatoria").GetBoolean());
        Assert.Equal(CodigoTrocaObrigatoria,
            (await (await mila.GetAsync(EndpointsDeExemplo.Item)).Content.ReadFromJsonAsync<ErroResposta>())!.Codigo);
    }
}
