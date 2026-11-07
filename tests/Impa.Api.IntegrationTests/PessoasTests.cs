using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Impa.Modules.Acesso.Application.Perfis;
using Impa.Modules.Acesso.Contracts;
using Impa.Modules.Pessoas.Application.Clientes;
using Impa.Modules.Pessoas.Application.Fornecedores;
using Impa.Modules.Pessoas.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Impa.Api.IntegrationTests;

/// <summary>Dublê de <see cref="IHistoricoDaPessoa"/> (Vendas, Compras e Financeiro chegam nos épicos 6, 11 e 12).</summary>
public sealed class HistoricoDeTeste : IHistoricoDaPessoa
{
    private readonly ConcurrentDictionary<Guid, int> _vinculos = new();

    /// <summary>Ex.: "Maria Souza tem 12 vendas" (cenário 14.3).</summary>
    public void Registrar(Guid pessoaId, int vinculos) => _vinculos[pessoaId] = vinculos;

    public Task<bool> TemHistoricoAsync(TipoDePessoa tipo, Guid pessoaId, CancellationToken ct) =>
        Task.FromResult(_vinculos.TryGetValue(pessoaId, out var n) && n > 0);
}

/// <summary>Guarda tudo o que a API registra em log, do nível Trace para cima (RN-14.6).</summary>
public sealed class LogCapturado : ILoggerProvider
{
    public ConcurrentQueue<string> Linhas { get; } = new();

    public ILogger CreateLogger(string categoria) => new Logger(categoria, Linhas);

    public void Dispose() { }

    private sealed class Logger(string categoria, ConcurrentQueue<string> linhas) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var estado = state is IEnumerable<KeyValuePair<string, object?>> pares
                ? string.Join(";", pares.Select(p => $"{p.Key}={p.Value}"))
                : string.Empty;
            linhas.Enqueue($"{categoria}|{formatter(state, exception)}|{estado}|{exception}");
        }
    }
}

public sealed class PessoasApiFactory : ImpaApiFactory
{
    public HistoricoDeTeste Historico { get; } = new();

    public LogCapturado Log { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(Log);
            // EF Core (comandos SQL, erros de gravação) e o código do IMPA em Trace, acima da configuração;
            // o Microsoft.AspNetCore fica no nível configurado (Warning), como em produção.
            logging.AddFilter<LogCapturado>("Microsoft.EntityFrameworkCore", LogLevel.Trace);
            logging.AddFilter<LogCapturado>("Impa", LogLevel.Trace);
        });
        builder.ConfigureServices(services => services.AddSingleton<IHistoricoDaPessoa>(Historico));
    }
}

/// <summary>Ajudantes dos testes da story 3-7 (o cliente HTTP se comporta como a SPA).</summary>
public abstract class PessoasTestsBase(PessoasApiFactory factory)
{
    protected PessoasApiFactory Factory { get; } = factory;

    protected const string Clientes = "/api/pessoas/clientes";
    protected const string Fornecedores = "/api/pessoas/fornecedores";
    protected const string SenhaOperador = "balcao-2026";

    protected sealed record ErroResposta(string Mensagem);

    protected async Task<ClienteApi> EntrarAsync(string usuario, string senha)
    {
        var cliente = await ClienteApi.AbrirAsync(Factory);
        Assert.Equal(HttpStatusCode.NoContent, (await cliente.EntrarAsync(usuario, senha)).StatusCode);
        return cliente;
    }

    protected Task<ClienteApi> AdminAsync() => EntrarAsync(ImpaApiFactory.AdminUsuario, ImpaApiFactory.AdminSenha);

    protected async Task<ClienteApi> OperadorAsync()
    {
        await Factory.CriarUsuarioAsync("carla", SenhaOperador, "Carla", PerfisPadrao.OperadorDeCaixa.Nome);
        return await EntrarAsync("carla", SenhaOperador);
    }

    protected static async Task<string> MensagemAsync(HttpResponseMessage resposta) =>
        (await resposta.Content.ReadFromJsonAsync<ErroResposta>())!.Mensagem;

    protected static async Task<string> ErroDeValidacaoAsync(HttpResponseMessage resposta, string campo)
    {
        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
        return json.GetProperty("errors").GetProperty(campo)[0].GetString()!;
    }

    protected static object Cliente(string nome, string? documento = null, string? telefone = null, string? email = null,
        string? endereco = null) => new { nome, documento, telefone, email, endereco, observacoes = (string?)null };

    protected static object Fornecedor(string razaoSocial, string? documento, string? nomeFantasia = null) =>
        new { razaoSocial, documento, nomeFantasia, telefone = (string?)null, email = (string?)null, contato = (string?)null };

    protected static async Task<ClienteDto> CriadoAsync(HttpResponseMessage resposta)
    {
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        return (await resposta.Content.ReadFromJsonAsync<ClienteDto>())!;
    }

    protected static async Task<List<ClienteDto>> ListaAsync(ClienteApi api, string url)
    {
        var resposta = await api.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        return (await resposta.Content.ReadFromJsonAsync<List<ClienteDto>>())!;
    }

}

/// <summary>Cenário 14.1 num banco próprio: o "Dado que não existe cliente com o CPF 529.982.247-25" vale de fato.</summary>
public class Cenario141Tests(PessoasApiFactory factory) : PessoasTestsBase(factory), IClassFixture<PessoasApiFactory>
{
    // ---- Cenário 14.1 (CA-1, CA-6) ----

    [Fact]
    public async Task Cenario_14_1_operador_cadastra_e_o_cliente_e_achado_pelo_nome_cpf_e_telefone()
    {
        var operador = await OperadorAsync();

        // Dado que não existe cliente com o CPF 529.982.247-25
        Assert.Empty(await ListaAsync(operador, $"{Clientes}?busca=529.982.247-25"));

        // Quando o operador cadastra o cliente "Maria Souza" com CPF 529.982.247-25 e telefone (31) 99999-0000
        var maria = await CriadoAsync(await operador.PostAsync(Clientes,
            Cliente("Maria Souza", "529.982.247-25", "(31) 99999-0000")));

        // Então o cliente é salvo
        Assert.Equal("Maria Souza", maria.Nome);
        Assert.Equal("529.982.247-25", maria.Documento);
        Assert.Equal("31999990000", maria.Telefone);
        Assert.True(maria.Ativo);

        // E é encontrado pela busca "maria", pelo CPF (com e sem máscara) ou pelo telefone — na tela...
        foreach (var busca in new[] { "maria", "MARIA", "souza maria", "529.982.247-25", "52998224725", "(31) 99999-0000", "31999990000" })
            Assert.Contains(await ListaAsync(operador, $"{Clientes}?busca={Uri.EscapeDataString(busca)}"), c => c.Id == maria.Id);

        // ... e pelo contrato que o PDV vai usar (épico 6).
        await using var escopo = Factory.Services.CreateAsyncScope();
        var consultas = escopo.ServiceProvider.GetRequiredService<IPessoasConsultas>();
        foreach (var busca in new[] { "maria", "529.982.247-25", "52998224725", "(31) 99999-0000" })
            Assert.Contains(await consultas.BuscarClientesAsync(busca, 10, CancellationToken.None), c => c.Id == maria.Id);
    }

}

/// <summary>Story 3-7 — clientes e fornecedores: cenários 14.1, 14.2 e 14.3, RN-14.1 a RN-14.6, de ponta a ponta.</summary>
public class PessoasTests(PessoasApiFactory factory) : PessoasTestsBase(factory), IClassFixture<PessoasApiFactory>
{
    [Fact]
    public async Task RN_14_3_busca_por_nome_sem_acento_e_em_qualquer_ordem_e_so_ativos_no_PDV()
    {
        var admin = await AdminAsync();
        var jose = await CriadoAsync(await admin.PostAsync(Clientes, Cliente("José Antônio Araújo")));
        var inativo = await CriadoAsync(await admin.PostAsync(Clientes, Cliente("José Inativo Araújo")));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"{Clientes}/{inativo.Id}/inativar")).StatusCode);

        var encontrados = await ListaAsync(admin, $"{Clientes}?busca={Uri.EscapeDataString("araujo jose")}");
        Assert.Contains(encontrados, c => c.Id == jose.Id);
        Assert.DoesNotContain(encontrados, c => c.Id == inativo.Id);
        Assert.Contains(await ListaAsync(admin, $"{Clientes}?busca=antonio&incluirInativos=true"), c => c.Id == jose.Id);
        Assert.Contains(await ListaAsync(admin, $"{Clientes}?busca=araujo&incluirInativos=true"), c => c.Id == inativo.Id);

        // Curinga do LIKE não vira "qualquer coisa".
        Assert.Empty(await ListaAsync(admin, $"{Clientes}?busca={Uri.EscapeDataString("%")}"));

        await using var escopo = Factory.Services.CreateAsyncScope();
        var pdv = await escopo.ServiceProvider.GetRequiredService<IPessoasConsultas>()
            .BuscarClientesAsync("araujo", 10, CancellationToken.None);
        Assert.Contains(pdv, c => c.Id == jose.Id);
        Assert.DoesNotContain(pdv, c => c.Id == inativo.Id);
    }

    // ---- Esquema do cenário 14.2 (CA-2, CA-4, CA-5) ----

    private async Task<ClienteApi> ComMariaAsync()
    {
        var admin = await AdminAsync();
        var existente = await ListaAsync(admin, $"{Clientes}?busca=52998224725&incluirInativos=true");
        if (existente.Count == 0)
            await CriadoAsync(await admin.PostAsync(Clientes, Cliente("Maria Souza", "529.982.247-25")));
        return admin;
    }

    [Fact]
    public async Task Cenario_14_2_cliente_sem_documento_e_salvo()
    {
        var admin = await ComMariaAsync();
        var joao = await CriadoAsync(await admin.PostAsync(Clientes, Cliente("João Lima", documento: null)));
        Assert.Null(joao.Documento);

        // Vários clientes sem documento convivem (índice único parcial).
        await CriadoAsync(await admin.PostAsync(Clientes, Cliente("João Lima", documento: "")));
    }

    [Fact]
    public async Task Cenario_14_2_cpf_repetido_e_recusado()
    {
        var admin = await ComMariaAsync();
        var resposta = await admin.PostAsync(Clientes, Cliente("João Lima", "529.982.247-25"));

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal("CPF já cadastrado para Maria Souza", await MensagemAsync(resposta));
    }

    [Fact]
    public async Task Cenario_14_2_cpf_invalido_e_recusado()
    {
        var admin = await ComMariaAsync();
        var resposta = await admin.PostAsync(Clientes, Cliente("João Lima", "111.111.111-11"));

        Assert.Equal("CPF inválido", await ErroDeValidacaoAsync(resposta, "Documento"));
    }

    [Fact]
    public async Task Cenario_14_2_fornecedor_sem_documento_e_recusado()
    {
        var admin = await ComMariaAsync();
        var resposta = await admin.PostAsync(Fornecedores, Fornecedor("Distribuidora Alfa", null));

        Assert.Equal("Informe o CNPJ ou CPF do fornecedor", await ErroDeValidacaoAsync(resposta, "Documento"));
    }

    [Fact]
    public async Task CNPJ_invalido_e_repetido_com_as_mensagens_decididas()
    {
        var admin = await AdminAsync();

        Assert.Equal("CNPJ inválido",
            await ErroDeValidacaoAsync(await admin.PostAsync(Clientes, Cliente("Papelaria Beta", "11.222.333/0001-82")), "Documento"));
        Assert.Equal("CNPJ inválido",
            await ErroDeValidacaoAsync(await admin.PostAsync(Fornecedores, Fornecedor("Alfa", "11.222.333/0001-82")), "Documento"));

        await CriadoAsync(await admin.PostAsync(Clientes, Cliente("Papelaria Beta", "11.222.333/0001-81")));
        var clienteRepetido = await admin.PostAsync(Clientes, Cliente("Outra", "11222333000181"));
        Assert.Equal(HttpStatusCode.Conflict, clienteRepetido.StatusCode);
        Assert.Equal("CNPJ já cadastrado para Papelaria Beta", await MensagemAsync(clienteRepetido));

        // O mesmo CNPJ pode ser de um cliente e de um fornecedor: a unicidade é por tipo.
        var alfa = await admin.PostAsync(Fornecedores, Fornecedor("Distribuidora Alfa", "11.222.333/0001-81", "Alfa"));
        Assert.Equal(HttpStatusCode.Created, alfa.StatusCode);
        var fornecedorRepetido = await admin.PostAsync(Fornecedores, Fornecedor("Beta", "11222333000181"));
        Assert.Equal(HttpStatusCode.Conflict, fornecedorRepetido.StatusCode);
        Assert.Equal("CNPJ já cadastrado para o fornecedor Distribuidora Alfa", await MensagemAsync(fornecedorRepetido));

        // Busca de fornecedores (RN-14.3): razão social, nome fantasia ou CNPJ.
        var id = (await alfa.Content.ReadFromJsonAsync<FornecedorDto>())!.Id;
        foreach (var busca in new[] { "distribuidora", "alfa", "11.222.333/0001-81", "11222333" })
        {
            var lista = await (await admin.GetAsync($"{Fornecedores}?busca={Uri.EscapeDataString(busca)}"))
                .Content.ReadFromJsonAsync<List<FornecedorDto>>();
            Assert.Contains(lista!, f => f.Id == id);
        }

        await using var escopo = Factory.Services.CreateAsyncScope();
        var porDocumento = await escopo.ServiceProvider.GetRequiredService<IPessoasConsultas>()
            .ObterFornecedorPorDocumentoAsync("11.222.333/0001-81", CancellationToken.None);
        Assert.Equal(id, porDocumento!.Id);
    }

    [Fact]
    public async Task Alterar_para_o_cpf_de_outro_cliente_e_recusado()
    {
        var admin = await ComMariaAsync();
        var joao = await CriadoAsync(await admin.PostAsync(Clientes, Cliente("João Alterado")));

        var resposta = await admin.PutAsync($"{Clientes}/{joao.Id}", Cliente("João Alterado", "52998224725"));

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal("CPF já cadastrado para Maria Souza", await MensagemAsync(resposta));
    }

    // ---- Cenário 14.3 e RN-14.4 (CA-3, CA-7) ----

    [Fact]
    public async Task Cenario_14_3_cliente_com_historico_so_e_inativado()
    {
        var admin = await AdminAsync();
        var maria = await CriadoAsync(await admin.PostAsync(Clientes, Cliente("Maria Histórico")));
        Factory.Historico.Registrar(maria.Id, 12); // Dado que "Maria Souza" tem 12 vendas

        // Quando o administrador tenta excluir o cliente
        var exclusao = await admin.DeleteAsync($"{Clientes}/{maria.Id}");

        // Então a exclusão é recusada com a mensagem "Este cliente tem histórico. Você pode inativá-lo"
        Assert.Equal(HttpStatusCode.Conflict, exclusao.StatusCode);
        Assert.Equal("Este cliente tem histórico. Você pode inativá-lo", await MensagemAsync(exclusao));
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"{Clientes}/{maria.Id}")).StatusCode);

        // ... e pode ser inativado (e reativado).
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"{Clientes}/{maria.Id}/inativar")).StatusCode);
        Assert.False((await (await admin.GetAsync($"{Clientes}/{maria.Id}")).Content.ReadFromJsonAsync<ClienteDto>())!.Ativo);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"{Clientes}/{maria.Id}/reativar")).StatusCode);
        Assert.True((await (await admin.GetAsync($"{Clientes}/{maria.Id}")).Content.ReadFromJsonAsync<ClienteDto>())!.Ativo);
    }

    [Fact]
    public async Task RN_14_4_cliente_sem_historico_e_excluido_e_fornecedor_com_historico_nao()
    {
        var admin = await AdminAsync();
        var semHistorico = await CriadoAsync(await admin.PostAsync(Clientes, Cliente("Cliente Avulso")));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{Clientes}/{semHistorico.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{Clientes}/{semHistorico.Id}")).StatusCode);

        var resposta = await admin.PostAsync(Fornecedores, Fornecedor("Gráfica Gama", "45.723.174/0001-10"));
        var gama = (await resposta.Content.ReadFromJsonAsync<FornecedorDto>())!;
        Factory.Historico.Registrar(gama.Id, 3);

        var exclusao = await admin.DeleteAsync($"{Fornecedores}/{gama.Id}");
        Assert.Equal(HttpStatusCode.Conflict, exclusao.StatusCode);
        Assert.Equal("Este fornecedor tem histórico. Você pode inativá-lo", await MensagemAsync(exclusao));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"{Fornecedores}/{gama.Id}/inativar")).StatusCode);
    }

    // ---- RN-14.5 (CA-8) ----

    [Fact]
    public async Task RN_14_5_operador_cadastra_e_consulta_mas_nao_inativa_exclui_nem_acessa_fornecedores()
    {
        var operador = await OperadorAsync();
        var cliente = await CriadoAsync(await operador.PostAsync(Clientes, Cliente("Cliente do Balcão")));
        Assert.Equal(HttpStatusCode.OK,
            (await operador.PutAsync($"{Clientes}/{cliente.Id}", Cliente("Cliente do Balcão Alterado"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await operador.GetAsync($"{Clientes}/{cliente.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await operador.DeleteAsync($"{Clientes}/{cliente.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operador.PostAsync($"{Clientes}/{cliente.Id}/inativar")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operador.PostAsync($"{Clientes}/{cliente.Id}/reativar")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operador.GetAsync(Fornecedores)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await operador.PostAsync(Fornecedores, Fornecedor("Distribuidora Ômega", "11.444.777/0001-61"))).StatusCode);

        // O Administrador tem as permissões novas (seed); o Operador, não.
        var meOperador = JsonDocument.Parse(await (await operador.GetAsync("/api/auth/me")).Content.ReadAsStringAsync()).RootElement;
        var permissoesOperador = meOperador.GetProperty("permissoes").EnumerateArray().Select(p => p.GetString()).ToList();
        Assert.DoesNotContain(Permissoes.ClientesInativar, permissoesOperador);
        Assert.DoesNotContain(Permissoes.FornecedoresGerenciar, permissoesOperador);

        var admin = await AdminAsync();
        var meAdmin = JsonDocument.Parse(await (await admin.GetAsync("/api/auth/me")).Content.ReadAsStringAsync()).RootElement;
        var permissoesAdmin = meAdmin.GetProperty("permissoes").EnumerateArray().Select(p => p.GetString()).ToList();
        Assert.Contains(Permissoes.ClientesInativar, permissoesAdmin);
        Assert.Contains(Permissoes.FornecedoresGerenciar, permissoesAdmin);
    }

    [Fact]
    public async Task RN_14_6_sem_sessao_ou_sem_clientes_ver_nao_ha_dados_de_clientes()
    {
        var anonimo = await ClienteApi.AbrirAsync(Factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync(Clientes)).StatusCode);

        // Perfil só com clientes.editar: não recebe dados pessoais nem na resposta do cadastro.
        var admin = await AdminAsync();
        var perfil = await admin.PostAsync("/api/perfis", new { nome = "Só edita clientes", permissoes = new[] { Permissoes.ClientesEditar } });
        Assert.Equal(HttpStatusCode.Created, perfil.StatusCode);
        await Factory.CriarUsuarioAsync("edu", "edita-2026", "Edu", "Só edita clientes");
        var edu = await EntrarAsync("edu", "edita-2026");

        Assert.Equal(HttpStatusCode.Forbidden, (await edu.GetAsync(Clientes)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await edu.PostAsync(Clientes, Cliente("Sem Ver", "529.982.247-25"))).StatusCode);
    }

    [Fact]
    public async Task RN_14_6_log_nao_contem_cpf_telefone_email_nem_endereco()
    {
        var admin = await AdminAsync();
        Factory.Log.Linhas.Clear();

        const string cpf = "390.533.447-05";
        const string telefone = "(31) 98765-4321";
        const string email = "lgpd.teste@exemplo.com";
        const string endereco = "Rua das Acácias, 742";

        var criado = await CriadoAsync(await admin.PostAsync(Clientes, Cliente("Cliente LGPD", cpf, telefone, email, endereco)));
        await ListaAsync(admin, $"{Clientes}?busca={Uri.EscapeDataString(cpf)}");
        await ListaAsync(admin, $"{Clientes}?busca={Uri.EscapeDataString(telefone)}");
        await admin.PutAsync($"{Clientes}/{criado.Id}", Cliente("Cliente LGPD", cpf, telefone, email, endereco));
        await admin.PostAsync(Clientes, Cliente("Repetido LGPD", cpf, telefone, email, endereco)); // 409
        await admin.PostAsync(Clientes, Cliente("Inválido LGPD", "390.533.447-06", telefone, email, endereco)); // 400

        var log = string.Join(Environment.NewLine, Factory.Log.Linhas);
        Assert.Contains(Factory.Log.Linhas, l => l.StartsWith("Microsoft.EntityFrameworkCore.Database.Command", StringComparison.Ordinal));
        foreach (var dado in new[] { cpf, "39053344705", "390533447", telefone, "31987654321", "98765", email, "lgpd.teste", endereco, "Acácias" })
            Assert.DoesNotContain(dado, log);
    }
}
