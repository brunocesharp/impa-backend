using System.Net;
using System.Net.Http.Json;
using Impa.Modules.Acesso.Application.Perfis;
using Impa.Modules.Acesso.Application.Terminais;
using Impa.Modules.Acesso.Contracts;

namespace Impa.Api.IntegrationTests;

/// <summary>Story 1-6, T5 — cenário 11.6 (CA-1) e RN-11.1 (CA-2): terminais e a política <c>RequireTerminal</c>.</summary>
public class TerminaisTests(ImpaApiFactory factory) : IClassFixture<ImpaApiFactory>, IAsyncLifetime
{
    private const string SenhaAna = "balcao2026";
    private const string CodigoNaoRegistrado = "terminal_nao_registrado";
    private const string MensagemNaoRegistrado = "Este computador não está registrado como terminal. Peça ao administrador";

    private sealed record ErroResposta(string Mensagem, string? Codigo);

    public async Task InitializeAsync() =>
        await factory.CriarUsuarioAsync("ana", SenhaAna, "Ana", PerfisPadrao.OperadorDeCaixa.Nome);

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Um dispositivo (navegador): os cookies, inclusive o do terminal, ficam nele entre logins.</summary>
    private Task<ClienteApi> DispositivoAsync() => ClienteApi.AbrirAsync(factory);

    private static async Task EntrarAsync(ClienteApi dispositivo, string usuario, string senha)
    {
        Assert.Equal(HttpStatusCode.NoContent, (await dispositivo.EntrarAsync(usuario, senha)).StatusCode);
        await dispositivo.GetAsync("/api/auth/me"); // token XSRF da identidade nova, como a SPA
    }

    private static Task EntrarComoAdminAsync(ClienteApi dispositivo) =>
        EntrarAsync(dispositivo, ImpaApiFactory.AdminUsuario, ImpaApiFactory.AdminSenha);

    private static Task EntrarComoAnaAsync(ClienteApi dispositivo) => EntrarAsync(dispositivo, "ana", SenhaAna);

    private static async Task TrocarParaAnaAsync(ClienteApi dispositivo)
    {
        Assert.Equal(HttpStatusCode.NoContent, (await dispositivo.SairAsync()).StatusCode);
        await dispositivo.GetAsync("/api/auth/me");
        await EntrarComoAnaAsync(dispositivo);
    }

    private static async Task<TerminalDto> RegistrarAsync(ClienteApi dispositivo, string nome)
    {
        var resposta = await dispositivo.PostAsync("/api/terminais", new { nome });
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        return (await resposta.Content.ReadFromJsonAsync<TerminalDto>())!;
    }

    private static string NomeUnico(string prefixo) => $"{prefixo} {Guid.NewGuid():N}"[..30];

    private static async Task AssertTerminalNaoRegistradoAsync(HttpResponseMessage resposta)
    {
        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        var erro = await resposta.Content.ReadFromJsonAsync<ErroResposta>();
        Assert.Equal(CodigoNaoRegistrado, erro!.Codigo);
        Assert.Equal(MensagemNaoRegistrado, erro.Mensagem);
    }

    // ---- CA-1: cenário 11.6 ----

    [Fact]
    public async Task Cenario_11_6_PDV_bloqueado_em_dispositivo_nao_registrado()
    {
        // Dado que o computador não foi registrado como terminal
        var computador = await DispositivoAsync();
        await EntrarComoAnaAsync(computador);

        // Quando o operador acessa o PDV nesse computador
        var pdv = await computador.PostAsync(EndpointsDeExemplo.PdvNoTerminal);
        var caixa = await computador.GetAsync(EndpointsDeExemplo.CaixaNoTerminal);
        var atual = await computador.GetAsync("/api/terminais/atual");

        // Então aparece a mensagem "Este computador não está registrado como terminal. Peça ao administrador"
        await AssertTerminalNaoRegistradoAsync(pdv);
        await AssertTerminalNaoRegistradoAsync(caixa);
        await AssertTerminalNaoRegistradoAsync(atual);
    }

    [Fact]
    public async Task Sem_sessao_PDV_responde_401_mesmo_com_terminal()
    {
        var anonimo = await DispositivoAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync(EndpointsDeExemplo.CaixaNoTerminal)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/terminais/atual")).StatusCode);
    }

    // ---- CA-2: RN-11.1 ----

    [Fact]
    public async Task Com_terminal_registrado_o_operador_acessa_PDV_e_caixa()
    {
        var computador = await DispositivoAsync();
        await EntrarComoAdminAsync(computador);
        var terminal = await RegistrarAsync(computador, NomeUnico("Balcão"));

        await TrocarParaAnaAsync(computador);

        Assert.Equal(HttpStatusCode.NoContent, (await computador.PostAsync(EndpointsDeExemplo.PdvNoTerminal)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await computador.GetAsync(EndpointsDeExemplo.CaixaNoTerminal)).StatusCode);
        var atual = await (await computador.GetAsync("/api/terminais/atual")).Content.ReadFromJsonAsync<TerminalAtualDto>();
        Assert.Equal(new TerminalAtualDto(terminal.Id, terminal.Nome), atual);
    }

    [Fact]
    public async Task Terminal_revogado_volta_a_receber_403()
    {
        var computador = await DispositivoAsync();
        await EntrarComoAdminAsync(computador);
        var terminal = await RegistrarAsync(computador, NomeUnico("Revogar"));
        Assert.Equal(HttpStatusCode.NoContent, (await computador.PostAsync(EndpointsDeExemplo.PdvNoTerminal)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await computador.PostAsync($"/api/terminais/{terminal.Id}/revogar")).StatusCode);

        await AssertTerminalNaoRegistradoAsync(await computador.PostAsync(EndpointsDeExemplo.PdvNoTerminal));
        await AssertTerminalNaoRegistradoAsync(await computador.GetAsync("/api/terminais/atual"));
        var lista = await (await computador.GetAsync("/api/terminais")).Content.ReadFromJsonAsync<List<TerminalDto>>();
        Assert.NotNull(lista!.Single(t => t.Id == terminal.Id).RevogadoEm);
    }

    [Fact]
    public async Task Nome_repetido_e_recusado()
    {
        var nome = NomeUnico("Celular Loja");
        var primeiro = await DispositivoAsync();
        await EntrarComoAdminAsync(primeiro);
        await RegistrarAsync(primeiro, nome);

        var segundo = await DispositivoAsync();
        await EntrarComoAdminAsync(segundo);
        var resposta = await segundo.PostAsync("/api/terminais", new { nome = nome.ToUpperInvariant() });

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal(MensagensTerminais.NomeEmUso, (await resposta.Content.ReadFromJsonAsync<ErroResposta>())!.Mensagem);
        await AssertTerminalNaoRegistradoAsync(await segundo.GetAsync("/api/terminais/atual")); // nada gravado no segundo
    }

    [Fact]
    public async Task Outro_dispositivo_nao_acessa_o_PDV_mas_as_demais_funcoes_funcionam()
    {
        var registrado = await DispositivoAsync();
        await EntrarComoAdminAsync(registrado);
        await RegistrarAsync(registrado, NomeUnico("Balcão"));

        var celularDaAna = await DispositivoAsync();
        await EntrarComoAnaAsync(celularDaAna);

        await AssertTerminalNaoRegistradoAsync(await celularDaAna.PostAsync(EndpointsDeExemplo.PdvNoTerminal));
        Assert.Equal(HttpStatusCode.OK, (await celularDaAna.GetAsync(EndpointsDeExemplo.Item)).StatusCode); // consulta de itens
        Assert.Equal(HttpStatusCode.OK, (await celularDaAna.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Terminal_valido_nao_dispensa_a_permissao_da_funcao()
    {
        var computador = await DispositivoAsync();
        await EntrarComoAdminAsync(computador);
        await RegistrarAsync(computador, NomeUnico("Balcão"));

        await TrocarParaAnaAsync(computador);

        // No terminal registrado, Ana (sem terminais.gerenciar) continua sem gerenciar terminais.
        Assert.Equal(HttpStatusCode.Forbidden, (await computador.GetAsync("/api/terminais")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await computador.PostAsync("/api/terminais", new { nome = "Meu" })).StatusCode);
    }

    /// <summary>Usuário de um perfil só de consulta de itens: sem <c>pdv.vender</c> nem <c>caixa.operar</c>.</summary>
    private async Task<(string Login, string Senha)> CriarUsuarioSemPdvECaixaAsync(ClienteApi admin)
    {
        var criado = await admin.PostAsync("/api/perfis",
            new { nome = NomeUnico("Só consulta"), permissoes = new[] { Permissoes.CatalogoVer } });
        Assert.Equal(HttpStatusCode.Created, criado.StatusCode);
        var perfil = (await criado.Content.ReadFromJsonAsync<PerfilDto>())!;
        var login = $"leo-{Guid.NewGuid():N}"[..20];
        const string senha = "consulta-2026";
        await factory.CriarUsuarioAsync(login, senha, "Léo", perfil.Nome);
        return (login, senha);
    }

    private static async Task AssertForbiddenComumAsync(HttpResponseMessage resposta)
    {
        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.DoesNotContain(CodigoNaoRegistrado, await resposta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Terminal_valido_nao_dispensa_a_permissao_do_PDV_e_do_caixa()
    {
        // Revisão 1, R3: terminal registrado + usuário sem pdv.vender/caixa.operar → 403 comum.
        var computador = await DispositivoAsync();
        await EntrarComoAdminAsync(computador);
        await RegistrarAsync(computador, NomeUnico("Balcão"));
        var (login, senha) = await CriarUsuarioSemPdvECaixaAsync(computador);

        Assert.Equal(HttpStatusCode.NoContent, (await computador.SairAsync()).StatusCode);
        await computador.GetAsync("/api/auth/me");
        await EntrarAsync(computador, login, senha);

        Assert.Equal(HttpStatusCode.OK, (await computador.GetAsync("/api/terminais/atual")).StatusCode); // terminal válido
        await AssertForbiddenComumAsync(await computador.PostAsync(EndpointsDeExemplo.PdvNoTerminal));
        await AssertForbiddenComumAsync(await computador.GetAsync(EndpointsDeExemplo.CaixaNoTerminal));
    }

    [Fact]
    public async Task Sem_permissao_e_sem_terminal_responde_o_403_comum_e_nao_o_do_terminal()
    {
        // Revisão 1, R2: registrar o terminal não resolveria, então a resposta não é terminal_nao_registrado.
        var admin = await DispositivoAsync();
        await EntrarComoAdminAsync(admin);
        var (login, senha) = await CriarUsuarioSemPdvECaixaAsync(admin);

        var naoRegistrado = await DispositivoAsync();
        await EntrarAsync(naoRegistrado, login, senha);

        await AssertForbiddenComumAsync(await naoRegistrado.PostAsync(EndpointsDeExemplo.PdvNoTerminal));
        await AssertForbiddenComumAsync(await naoRegistrado.GetAsync(EndpointsDeExemplo.CaixaNoTerminal));
        await AssertTerminalNaoRegistradoAsync(await naoRegistrado.GetAsync("/api/terminais/atual")); // só falta o terminal
    }

    // ---- T2: registro, cookie e gestão ----

    [Fact]
    public async Task Registro_grava_o_token_em_cookie_HttpOnly_Secure_SameSite_Strict_de_longa_duracao_e_nao_no_corpo()
    {
        var computador = await DispositivoAsync();
        await EntrarComoAdminAsync(computador);

        var resposta = await computador.PostAsync("/api/terminais", new { nome = NomeUnico("Cookie") });

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var cookie = computador.UltimosSetCookie["impa_terminal"].ToLowerInvariant();
        Assert.Contains("httponly", cookie);
        Assert.Contains("secure", cookie);
        Assert.Contains("samesite=strict", cookie);
        Assert.Contains("path=/", cookie);
        Assert.Contains("expires=", cookie);
        var token = computador.UltimosSetCookie["impa_terminal"].Split(';')[0]["impa_terminal=".Length..];
        Assert.NotNull(TokenDoTerminal.HashSeValido(token));
        Assert.DoesNotContain(token, await resposta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Dispositivo_ja_registrado_nao_e_registrado_de_novo()
    {
        var computador = await DispositivoAsync();
        await EntrarComoAdminAsync(computador);
        var nome = NomeUnico("Único");
        await RegistrarAsync(computador, nome);

        var resposta = await computador.PostAsync("/api/terminais", new { nome = NomeUnico("Outro") });

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal(MensagensTerminais.DispositivoJaRegistrado(nome), (await resposta.Content.ReadFromJsonAsync<ErroResposta>())!.Mensagem);
    }

    [Fact]
    public async Task Lista_marca_o_terminal_deste_dispositivo_e_quem_registrou()
    {
        var computador = await DispositivoAsync();
        await EntrarComoAdminAsync(computador);
        var terminal = await RegistrarAsync(computador, NomeUnico("Lista"));

        var lista = await (await computador.GetAsync("/api/terminais")).Content.ReadFromJsonAsync<List<TerminalDto>>();

        var deste = lista!.Single(t => t.Id == terminal.Id);
        Assert.True(deste.Atual);
        Assert.Equal(ImpaApiFactory.AdminNome, deste.CriadoPor);
        Assert.Null(deste.RevogadoEm);
        Assert.All(lista!.Where(t => t.Id != terminal.Id), t => Assert.False(t.Atual));
    }

    [Fact]
    public async Task Renomear_e_validacoes()
    {
        var computador = await DispositivoAsync();
        await EntrarComoAdminAsync(computador);
        var terminal = await RegistrarAsync(computador, NomeUnico("Antigo"));
        var novoNome = NomeUnico("Novo");

        var renomeado = await computador.PutAsync($"/api/terminais/{terminal.Id}", new { nome = $"  {novoNome}  " });
        Assert.Equal(HttpStatusCode.OK, renomeado.StatusCode);
        Assert.Equal(novoNome, (await renomeado.Content.ReadFromJsonAsync<TerminalDto>())!.Nome);

        Assert.Equal(HttpStatusCode.BadRequest, (await computador.PutAsync($"/api/terminais/{terminal.Id}", new { nome = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await computador.PutAsync($"/api/terminais/{Guid.NewGuid()}", new { nome = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await computador.PostAsync($"/api/terminais/{Guid.NewGuid()}/revogar")).StatusCode);
    }

    [Fact]
    public async Task Revogado_libera_o_nome_e_o_dispositivo_para_novo_registro()
    {
        var computador = await DispositivoAsync();
        await EntrarComoAdminAsync(computador);
        var nome = NomeUnico("Reuso");
        var antigo = await RegistrarAsync(computador, nome);
        Assert.Equal(HttpStatusCode.NoContent, (await computador.PostAsync($"/api/terminais/{antigo.Id}/revogar")).StatusCode);

        var novo = await RegistrarAsync(computador, nome);

        Assert.NotEqual(antigo.Id, novo.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await computador.PostAsync(EndpointsDeExemplo.PdvNoTerminal)).StatusCode);
    }
}
