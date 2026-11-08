using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Impa.Modules.Acesso.Application.Perfis;
using Impa.Modules.Acesso.Contracts;
using Impa.Modules.Financeiro.Application.Categorias;
using Impa.Modules.Financeiro.Application.Contas;
using Impa.Modules.Financeiro.Application.FormasDePagamento;
using Impa.Modules.Pessoas.Application.Fornecedores;
using Microsoft.Extensions.DependencyInjection;

namespace Impa.Api.IntegrationTests;

/// <summary>
/// Story 11-1 — categorias, lançamento e parcelamento de contas (cenário 23.1, RN-23.1 a RN-23.3), a porta
/// <c>IHistoricoDaPessoa</c> implementada pelo Financeiro (RN-14.4) e as permissões <c>financeiro.ver</c> / <c>financeiro.editar</c>.
/// Story 11-2 — formas de pagamento (RN-23.4), pagamento total ou parcial (cenário 23.2, RN-23.5), estorno (cenário 23.5)
/// e a conta com pagamento (RN-23.7).
/// </summary>
public class FinanceiroTests(ImpaApiFactory factory) : IClassFixture<ImpaApiFactory>
{
    private const string Categorias = "/api/financeiro/categorias";
    private const string Contas = "/api/financeiro/contas";
    private const string Fornecedores = "/api/pessoas/fornecedores";

    private sealed record ErroResposta(string Mensagem);

    private async Task<ClienteApi> EntrarAsync(string usuario, string senha)
    {
        var cliente = await ClienteApi.AbrirAsync(factory);
        Assert.Equal(HttpStatusCode.NoContent, (await cliente.EntrarAsync(usuario, senha)).StatusCode);
        return cliente;
    }

    private Task<ClienteApi> AdminAsync() => EntrarAsync(ImpaApiFactory.AdminUsuario, ImpaApiFactory.AdminSenha);

    /// <summary>CNPJ válido a partir dos 12 primeiros dígitos (cada teste usa o seu fornecedor).</summary>
    private static string Cnpj(string doze)
    {
        static int Digito(string numeros, int[] pesos) =>
            numeros.Select((c, i) => (c - '0') * pesos[i]).Sum() % 11 is var resto && resto < 2 ? 0 : 11 - resto;

        var primeiro = Digito(doze, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        var treze = doze + primeiro;
        return treze + Digito(treze, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
    }

    private static async Task<FornecedorDto> FornecedorAsync(ClienteApi admin, string razaoSocial, string cnpj)
    {
        var resposta = await admin.PostAsync(Fornecedores,
            new { razaoSocial, documento = cnpj, nomeFantasia = (string?)null, telefone = (string?)null, email = (string?)null, contato = (string?)null });
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        return (await resposta.Content.ReadFromJsonAsync<FornecedorDto>())!;
    }

    private static async Task<CategoriaDto> CategoriaAsync(ClienteApi admin, string nome)
    {
        var resposta = await admin.PostAsync(Categorias, new { nome });
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        return (await resposta.Content.ReadFromJsonAsync<CategoriaDto>())!;
    }

    private static async Task<List<T>> ListaAsync<T>(ClienteApi api, string url)
    {
        var resposta = await api.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        return (await resposta.Content.ReadFromJsonAsync<List<T>>())!;
    }

    private static Task<HttpResponseMessage> LancarAsync(ClienteApi admin, string descricao, Guid categoriaId, decimal valor,
        string vencimento, int parcelas = 1, string? fornecedorDocumento = null) =>
        admin.PostAsync(Contas, new
        {
            descricao, categoriaId, valor, vencimento, parcelas, fornecedorDocumento,
            numeroDocumento = (string?)null, observacao = (string?)null,
        });

    // ---- Cenário 23.1 (CA-1, CA-4) ----

    [Fact]
    public async Task Cenario_23_1_parcelar_uma_conta_com_diferenca_de_centavos()
    {
        var admin = await AdminAsync();
        var alfa = await FornecedorAsync(admin, "Distribuidora Alfa", Cnpj("231000000001"));
        var mercadorias = (await ListaAsync<CategoriaDto>(admin, Categorias)).Single(c => c.Nome == "Compra de mercadorias");

        // Dado que não existem contas da "Distribuidora Alfa"
        Assert.DoesNotContain(await ListaAsync<ContaDto>(admin, Contas), c => c.Fornecedor?.Id == alfa.Id);

        // Quando o administrador lança "Compra de mochilas" de R$ 1.000,00 em 3 parcelas com primeiro vencimento em 31/01/2027
        var resposta = await LancarAsync(admin, "Compra de mochilas", mercadorias.Id, 1000.00m, "2027-01-31", 3, alfa.Documento);

        // Então são criadas as parcelas
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        (string Parcela, decimal Valor, DateOnly Vencimento)[] esperadas =
        [
            ("1/3", 333.34m, new DateOnly(2027, 1, 31)),
            ("2/3", 333.33m, new DateOnly(2027, 2, 28)),
            ("3/3", 333.33m, new DateOnly(2027, 3, 31)),
        ];
        var criadas = (await resposta.Content.ReadFromJsonAsync<List<ContaDto>>())!;
        Assert.Equal(esperadas, criadas.Select(c => ($"{c.Parcela}/{c.TotalDeParcelas}", c.Valor, c.Vencimento)));

        var daAlfa = (await ListaAsync<ContaDto>(admin, Contas)).Where(c => c.Fornecedor?.Id == alfa.Id).ToList();
        Assert.Equal(esperadas, daAlfa.Select(c => ($"{c.Parcela}/{c.TotalDeParcelas}", c.Valor, c.Vencimento)));
        Assert.All(daAlfa, c =>
        {
            Assert.Equal("Compra de mochilas", c.Descricao);
            Assert.Equal("Compra de mercadorias", c.Categoria);
            Assert.Equal("Distribuidora Alfa", c.Fornecedor!.RazaoSocial);
            Assert.Equal(criadas[0].ParcelamentoId, c.ParcelamentoId);
        });
        Assert.NotNull(criadas[0].ParcelamentoId);
    }

    // ---- RN-23.2 (CA-3) ----

    [Fact]
    public async Task RN_23_2_descricao_categoria_valor_e_vencimento_sao_obrigatorios()
    {
        var admin = await AdminAsync();

        var resposta = await admin.PostAsync(Contas, new { descricao = " ", valor = 0 });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        var erros = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement.GetProperty("errors");
        Assert.Equal("Informe a descrição", erros.GetProperty("Descricao")[0].GetString());
        Assert.Equal("Informe a categoria", erros.GetProperty("CategoriaId")[0].GetString());
        Assert.Equal("O valor precisa ser maior que zero", erros.GetProperty("Valor")[0].GetString());
        Assert.Equal("Informe o vencimento", erros.GetProperty("Vencimento")[0].GetString());
    }

    [Fact]
    public async Task RN_23_2_fornecedor_numero_do_documento_e_observacao_sao_opcionais_e_o_fornecedor_precisa_existir()
    {
        var admin = await AdminAsync();
        var aluguel = (await ListaAsync<CategoriaDto>(admin, Categorias)).Single(c => c.Nome == "Aluguel");

        var semFornecedor = await LancarAsync(admin, "Aluguel novembro", aluguel.Id, 2000m, "2026-11-10");
        var conta = Assert.Single((await semFornecedor.Content.ReadFromJsonAsync<List<ContaDto>>())!);
        Assert.Null(conta.Fornecedor);
        Assert.Null(conta.ParcelamentoId);
        Assert.Equal((1, 1), (conta.Parcela, conta.TotalDeParcelas));

        var inexistente = await LancarAsync(admin, "Aluguel dezembro", aluguel.Id, 2000m, "2026-12-10",
            fornecedorDocumento: Cnpj("231000000099"));
        Assert.Equal(HttpStatusCode.BadRequest, inexistente.StatusCode);
        var erros = JsonDocument.Parse(await inexistente.Content.ReadAsStringAsync()).RootElement.GetProperty("errors");
        Assert.Equal("Fornecedor não encontrado", erros.GetProperty("FornecedorDocumento")[0].GetString());
    }

    [Fact]
    public async Task Alterar_e_excluir_uma_parcela()
    {
        var admin = await AdminAsync();
        var energia = (await ListaAsync<CategoriaDto>(admin, Categorias)).Single(c => c.Nome == "Energia");
        var parcelas = (await (await LancarAsync(admin, "Energia trimestre", energia.Id, 900m, "2027-02-15", 3))
            .Content.ReadFromJsonAsync<List<ContaDto>>())!;

        var alterada = await admin.PutAsync($"{Contas}/{parcelas[1].Id}", new
        {
            descricao = "Energia março", categoriaId = energia.Id, valor = 310.50m, vencimento = "2027-03-20",
            fornecedorDocumento = (string?)null, numeroDocumento = "FAT-3", observacao = "Reajuste",
        });
        Assert.Equal(HttpStatusCode.OK, alterada.StatusCode);
        var obtida = (await (await admin.GetAsync($"{Contas}/{parcelas[1].Id}")).Content.ReadFromJsonAsync<ContaDto>())!;
        Assert.Equal(("Energia março", 310.50m, new DateOnly(2027, 3, 20), "FAT-3", "Reajuste", 2, 3),
            (obtida.Descricao, obtida.Valor, obtida.Vencimento, obtida.NumeroDocumento, obtida.Observacao, obtida.Parcela, obtida.TotalDeParcelas));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{Contas}/{parcelas[2].Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{Contas}/{parcelas[2].Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"{Contas}/{parcelas[2].Id}")).StatusCode);
    }

    [Fact]
    public async Task RN_23_2_alterar_com_corpo_invalido_da_400_com_as_mensagens_por_campo()
    {
        var admin = await AdminAsync();
        var aluguel = (await ListaAsync<CategoriaDto>(admin, Categorias)).Single(c => c.Nome == "Aluguel");
        var conta = (await (await LancarAsync(admin, "Aluguel janeiro", aluguel.Id, 2000m, "2027-01-10"))
            .Content.ReadFromJsonAsync<List<ContaDto>>())!.Single();

        var resposta = await admin.PutAsync($"{Contas}/{conta.Id}", new { descricao = " ", valor = 0 });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        var erros = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement.GetProperty("errors");
        Assert.Equal("Informe a descrição", erros.GetProperty("Descricao")[0].GetString());
        Assert.Equal("Informe a categoria", erros.GetProperty("CategoriaId")[0].GetString());
        Assert.Equal("O valor precisa ser maior que zero", erros.GetProperty("Valor")[0].GetString());
        Assert.Equal("Informe o vencimento", erros.GetProperty("Vencimento")[0].GetString());
    }

    [Fact]
    public async Task RN_23_2_valor_e_vencimento_fora_da_faixa_dao_400_no_campo()
    {
        var admin = await AdminAsync();
        var impostos = (await ListaAsync<CategoriaDto>(admin, Categorias)).Single(c => c.Nome == "Impostos");

        var noTeto = await LancarAsync(admin, "Imposto no teto", impostos.Id, 999_999_999_999.99m, "2100-12-31");
        Assert.Equal(HttpStatusCode.Created, noTeto.StatusCode);

        var acimaDoTeto = await LancarAsync(admin, "Imposto acima do teto", impostos.Id, 1_000_000_000_000m, "2027-01-10");
        Assert.Equal(HttpStatusCode.BadRequest, acimaDoTeto.StatusCode);
        var erroDoValor = JsonDocument.Parse(await acimaDoTeto.Content.ReadAsStringAsync()).RootElement.GetProperty("errors");
        Assert.Equal("O valor pode ser de no máximo R$ 999.999.999.999,99", erroDoValor.GetProperty("Valor")[0].GetString());

        var foraDaFaixa = await LancarAsync(admin, "Imposto em 9999", impostos.Id, 100m, "9999-12-31", 3);
        Assert.Equal(HttpStatusCode.BadRequest, foraDaFaixa.StatusCode);
        var erroDaData = JsonDocument.Parse(await foraDaFaixa.Content.ReadAsStringAsync()).RootElement.GetProperty("errors");
        Assert.Equal("Informe um vencimento entre 2000 e 2100", erroDaData.GetProperty("Vencimento")[0].GetString());
    }

    [Fact]
    public async Task RN_23_2_fornecedor_que_mudou_o_CNPJ_no_Pessoas_continua_na_conta_alterada()
    {
        var admin = await AdminAsync();
        var delta = await FornecedorAsync(admin, "Distribuidora Delta", Cnpj("231000000004"));
        var mercadorias = (await ListaAsync<CategoriaDto>(admin, Categorias)).Single(c => c.Nome == "Compra de mercadorias");
        var conta = (await (await LancarAsync(admin, "Compra de cadernos", mercadorias.Id, 640m, "2027-02-05", fornecedorDocumento: delta.Documento))
            .Content.ReadFromJsonAsync<List<ContaDto>>())!.Single();

        // O CNPJ do fornecedor é corrigido no Pessoas.
        var novoCnpj = Cnpj("231000000005");
        var corrigido = await admin.PutAsync($"{Fornecedores}/{delta.Id}", new
        {
            razaoSocial = "Distribuidora Delta", documento = novoCnpj, nomeFantasia = (string?)null,
            telefone = (string?)null, email = (string?)null, contato = (string?)null,
        });
        Assert.Equal(HttpStatusCode.OK, corrigido.StatusCode);

        // A tela manda de volta o documento gravado na conta.
        var alterada = await admin.PutAsync($"{Contas}/{conta.Id}", new
        {
            descricao = "Compra de cadernos e lápis", categoriaId = mercadorias.Id, valor = 640m, vencimento = "2027-02-05",
            fornecedorDocumento = conta.Fornecedor!.Documento, numeroDocumento = (string?)null, observacao = (string?)null,
        });

        Assert.Equal(HttpStatusCode.OK, alterada.StatusCode);
        var obtida = (await (await admin.GetAsync($"{Contas}/{conta.Id}")).Content.ReadFromJsonAsync<ContaDto>())!;
        Assert.Equal("Compra de cadernos e lápis", obtida.Descricao);
        Assert.Equal(delta.Id, obtida.Fornecedor!.Id);
    }

    // ---- RN-23.1 (CA-2) ----

    [Fact]
    public async Task RN_23_1_nome_da_categoria_unico_sem_diferenciar_maiusculas_tambem_no_banco()
    {
        _ = factory.CriarCliente(); // Host no ar: migrações e seed ("Energia" já existe)
        await using var escopo = factory.Services.CreateAsyncScope();
        var repositorio = escopo.ServiceProvider.GetRequiredService<ICategoriasRepository>();

        // Direto pelo repositório, sem a consulta prévia do handler: quem recusa é o índice único.
        Assert.False(await repositorio.AdicionarAsync(
            Impa.Modules.Financeiro.Domain.CategoriaFinanceira.Criar("energia", DateTimeOffset.UtcNow), CancellationToken.None));
        Assert.True(await repositorio.AdicionarAsync(
            Impa.Modules.Financeiro.Domain.CategoriaFinanceira.Criar("Energia solar", DateTimeOffset.UtcNow), CancellationToken.None));
    }

    [Fact]
    public async Task RN_23_1_exemplos_ja_cadastrados_e_categoria_com_contas_nao_e_excluida_so_inativada()
    {
        var admin = await AdminAsync();
        var nomes = (await ListaAsync<CategoriaDto>(admin, $"{Categorias}?incluirInativas=true")).Select(c => c.Nome).ToList();
        Assert.Subset(nomes.ToHashSet(), new HashSet<string> { "Compra de mercadorias", "Aluguel", "Energia", "Internet", "Impostos" });

        var manutencao = await CategoriaAsync(admin, "Manutenção");
        var limpeza = await CategoriaAsync(admin, "Limpeza");
        Assert.Equal(HttpStatusCode.Created, (await LancarAsync(admin, "Conserto do ar", manutencao.Id, 450m, "2026-12-01")).StatusCode);

        var excluir = await admin.DeleteAsync($"{Categorias}/{manutencao.Id}");
        Assert.Equal(HttpStatusCode.Conflict, excluir.StatusCode);
        Assert.Equal("Esta categoria tem contas. Você pode inativá-la", (await excluir.Content.ReadFromJsonAsync<ErroResposta>())!.Mensagem);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"{Categorias}/{manutencao.Id}/inativar")).StatusCode);
        Assert.DoesNotContain(await ListaAsync<CategoriaDto>(admin, Categorias), c => c.Id == manutencao.Id);
        Assert.Contains(new CategoriaDto(manutencao.Id, "Manutenção", false), await ListaAsync<CategoriaDto>(admin, $"{Categorias}?incluirInativas=true"));

        // Categoria inativa não recebe conta nova.
        var naInativa = await LancarAsync(admin, "Troca de lâmpadas", manutencao.Id, 80m, "2026-12-05");
        Assert.Equal(HttpStatusCode.BadRequest, naInativa.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{Categorias}/{limpeza.Id}")).StatusCode);
        Assert.DoesNotContain(await ListaAsync<CategoriaDto>(admin, $"{Categorias}?incluirInativas=true"), c => c.Id == limpeza.Id);

        var repetida = await admin.PostAsync(Categorias, new { nome = "aluguel" });
        Assert.Equal(HttpStatusCode.Conflict, repetida.StatusCode);
        Assert.Equal("Já existe a categoria Aluguel", (await repetida.Content.ReadFromJsonAsync<ErroResposta>())!.Mensagem);
    }

    // ---- RN-14.4 pela porta IHistoricoDaPessoa (CA-3) ----

    [Fact]
    public async Task Fornecedor_com_conta_nao_e_excluido_no_Pessoas_so_inativado()
    {
        var admin = await AdminAsync();
        var beta = await FornecedorAsync(admin, "Papelaria Beta", Cnpj("231000000002"));
        var gama = await FornecedorAsync(admin, "Gráfica Gama", Cnpj("231000000003"));
        var internet = (await ListaAsync<CategoriaDto>(admin, Categorias)).Single(c => c.Nome == "Internet");
        Assert.Equal(HttpStatusCode.Created,
            (await LancarAsync(admin, "Internet dezembro", internet.Id, 120m, "2026-12-15", fornecedorDocumento: beta.Documento)).StatusCode);

        var excluir = await admin.DeleteAsync($"{Fornecedores}/{beta.Id}");
        Assert.Equal(HttpStatusCode.Conflict, excluir.StatusCode);
        Assert.Equal("Este fornecedor tem histórico. Você pode inativá-lo", (await excluir.Content.ReadFromJsonAsync<ErroResposta>())!.Mensagem);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"{Fornecedores}/{beta.Id}/inativar")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{Fornecedores}/{gama.Id}")).StatusCode);
    }

    // ---- Permissões (decisão de 07/11/2026) ----

    [Fact]
    public async Task Consultar_com_financeiro_ver_e_lancar_alterar_e_excluir_so_com_financeiro_editar()
    {
        var admin = await AdminAsync();
        var criado = await admin.PostAsync("/api/perfis", new { nome = "Conferência financeira", permissoes = new[] { Permissoes.FinanceiroVer } });
        Assert.Equal(HttpStatusCode.Created, criado.StatusCode);
        await factory.CriarUsuarioAsync("joana", "conferencia-2026", "Joana", "Conferência financeira");
        await factory.CriarUsuarioAsync("carla", "balcao-2026", "Carla", PerfisPadrao.OperadorDeCaixa.Nome);
        var joana = await EntrarAsync("joana", "conferencia-2026");
        var carla = await EntrarAsync("carla", "balcao-2026");
        var impostos = (await ListaAsync<CategoriaDto>(admin, Categorias)).Single(c => c.Nome == "Impostos");
        var conta = (await (await LancarAsync(admin, "Simples nacional", impostos.Id, 780m, "2026-12-20"))
            .Content.ReadFromJsonAsync<List<ContaDto>>())!.Single();

        // Operador de caixa: sem financeiro.ver.
        Assert.Equal(HttpStatusCode.Forbidden, (await carla.GetAsync(Contas)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await carla.GetAsync(Categorias)).StatusCode);

        // Só financeiro.ver: consulta, mas não lança, altera nem exclui.
        Assert.Equal(HttpStatusCode.OK, (await joana.GetAsync(Contas)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await joana.GetAsync($"{Contas}/{conta.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await joana.GetAsync(Categorias)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await LancarAsync(joana, "Taxa", impostos.Id, 10m, "2026-12-20")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await joana.PutAsync($"{Contas}/{conta.Id}", new { descricao = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await joana.DeleteAsync($"{Contas}/{conta.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await joana.PostAsync(Categorias, new { nome = "Taxas" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await joana.DeleteAsync($"{Categorias}/{impostos.Id}")).StatusCode);

        // O Administrador recebe a permissão nova (tem todas) e ela aparece no /api/auth/me.
        var me = JsonDocument.Parse(await (await admin.GetAsync("/api/auth/me")).Content.ReadAsStringAsync()).RootElement;
        Assert.Contains(Permissoes.FinanceiroEditar, me.GetProperty("permissoes").EnumerateArray().Select(p => p.GetString()));
    }

    // ==== Story 11-2 — pagar e estornar ====

    private const string Formas = "/api/financeiro/formas-pagamento";

    private static async Task<ContaDto> ContaAsync(ClienteApi admin, string descricao, string categoria, decimal valor,
        string vencimento = "2026-11-10", string? fornecedorDocumento = null, string? numeroDocumento = null)
    {
        var categoriaId = (await ListaAsync<CategoriaDto>(admin, Categorias)).Single(c => c.Nome == categoria).Id;
        var resposta = await admin.PostAsync(Contas, new
        {
            descricao, categoriaId, valor, vencimento, parcelas = 1, fornecedorDocumento, numeroDocumento, observacao = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        return (await resposta.Content.ReadFromJsonAsync<List<ContaDto>>())!.Single();
    }

    private static async Task<Guid> FormaAsync(ClienteApi admin, string nome) =>
        (await ListaAsync<FormaDePagamentoDto>(admin, Formas)).Single(f => f.Nome == nome).Id;

    private static Task<HttpResponseMessage> PagarAsync(ClienteApi api, Guid contaId, decimal valor, string data, Guid formaDePagamentoId) =>
        api.PostAsync($"{Contas}/{contaId}/baixas", new { data, valor, formaDePagamentoId });

    private static async Task<ContaDto> ObterAsync(ClienteApi api, Guid contaId) =>
        (await (await api.GetAsync($"{Contas}/{contaId}")).Content.ReadFromJsonAsync<ContaDto>())!;

    // ---- Cenário 23.2 (CA-1, CA-4) ----

    [Theory]
    [InlineData("500.00", "paga", "0")]
    [InlineData("200.00", "parcialmente-paga", "300.00")]
    public async Task Cenario_23_2_registrar_o_pagamento_de_uma_conta_de_500(string valor, string situacao, string saldo)
    {
        var admin = await AdminAsync();
        var pix = await FormaAsync(admin, "Pix");
        // Dado que a conta "Aluguel novembro" tem saldo R$ 500,00
        var conta = await ContaAsync(admin, "Aluguel novembro", "Aluguel", 500m);
        Assert.Equal((500m, "sem-pagamento"), (conta.Saldo, conta.SituacaoDoPagamento));

        // Quando o administrador paga <valor> em 05/11/2026 via Pix
        var resposta = await PagarAsync(admin, conta.Id, Dec(valor), "2026-11-05", pix);

        // Então <resultado>
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var paga = await ObterAsync(admin, conta.Id);
        Assert.Equal((situacao, Dec(saldo), Dec(valor)), (paga.SituacaoDoPagamento, paga.Saldo, paga.ValorPago));
        var baixa = Assert.Single(paga.Baixas);
        Assert.Equal((new DateOnly(2026, 11, 5), Dec(valor), "Pix", false), (baixa.Data, baixa.Valor, baixa.FormaDePagamento, baixa.Estornada));
    }

    [Fact]
    public async Task Cenario_23_2_pagar_600_numa_conta_de_500_e_recusado()
    {
        var admin = await AdminAsync();
        var conta = await ContaAsync(admin, "Aluguel novembro", "Aluguel", 500m);

        var resposta = await PagarAsync(admin, conta.Id, 600m, "2026-11-05", await FormaAsync(admin, "Pix"));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        var erros = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement.GetProperty("errors");
        Assert.Equal("Valor maior que o saldo da conta (R$ 500,00)", erros.GetProperty("Valor")[0].GetString());
        Assert.Equal((500m, "sem-pagamento"), ((await ObterAsync(admin, conta.Id)).Saldo, (await ObterAsync(admin, conta.Id)).SituacaoDoPagamento));
    }

    [Fact]
    public async Task RN_23_5_data_valor_e_forma_obrigatorios_e_pagamentos_parciais_ate_quitar()
    {
        var admin = await AdminAsync();
        var conta = await ContaAsync(admin, "Internet novembro", "Internet", 1000m);
        var boleto = await FormaAsync(admin, "Boleto");

        var vazio = await admin.PostAsync($"{Contas}/{conta.Id}/baixas", new { });
        Assert.Equal(HttpStatusCode.BadRequest, vazio.StatusCode);
        var erros = JsonDocument.Parse(await vazio.Content.ReadAsStringAsync()).RootElement.GetProperty("errors");
        Assert.Equal("Informe a data do pagamento", erros.GetProperty("Data")[0].GetString());
        Assert.Equal("O valor precisa ser maior que zero", erros.GetProperty("Valor")[0].GetString());
        Assert.Equal("Informe a forma de pagamento", erros.GetProperty("FormaDePagamentoId")[0].GetString());

        Assert.Equal(HttpStatusCode.Created, (await PagarAsync(admin, conta.Id, 400m, "2026-11-05", boleto)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PagarAsync(admin, conta.Id, 600m, "2026-11-06", boleto)).StatusCode);
        var quitada = await ObterAsync(admin, conta.Id);
        Assert.Equal((0m, "paga", 2), (quitada.Saldo, quitada.SituacaoDoPagamento, quitada.Baixas.Count));

        var depois = await PagarAsync(admin, conta.Id, 0.01m, "2026-11-07", boleto);
        var erroDepois = JsonDocument.Parse(await depois.Content.ReadAsStringAsync()).RootElement.GetProperty("errors");
        Assert.Equal("Valor maior que o saldo da conta (R$ 0,00)", erroDepois.GetProperty("Valor")[0].GetString());

        Assert.Equal(HttpStatusCode.NotFound, (await PagarAsync(admin, Guid.CreateVersion7(), 10m, "2026-11-05", boleto)).StatusCode);
    }

    // ---- Cenário 23.5 (CA-2, CA-5) ----

    [Fact]
    public async Task Cenario_23_5_estornar_uma_baixa()
    {
        var admin = await AdminAsync();
        // Dado que a conta "Energia outubro" foi paga por engano com R$ 350,00
        var conta = await ContaAsync(admin, "Energia outubro", "Energia", 350m, "2026-10-20");
        Assert.Equal(HttpStatusCode.Created, (await PagarAsync(admin, conta.Id, 350m, "2026-11-05", await FormaAsync(admin, "Pix"))).StatusCode);
        var baixa = (await ObterAsync(admin, conta.Id)).Baixas.Single();

        // Quando o administrador estorna o pagamento com o motivo "Pagamento lançado na conta errada"
        var resposta = await admin.PostAsync($"/api/financeiro/baixas/{baixa.Id}/estorno", new { motivo = "Pagamento lançado na conta errada" });

        // Então a conta volta a ter saldo R$ 350,00 e a situação conforme o vencimento (a situação pelo vencimento é da 11.3)
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var estornada = await ObterAsync(admin, conta.Id);
        Assert.Equal((350m, 0m, "sem-pagamento"), (estornada.Saldo, estornada.ValorPago, estornada.SituacaoDoPagamento));
        // E o estorno fica registrado
        var registro = Assert.Single(estornada.Baixas);
        Assert.Equal((baixa.Id, true, ImpaApiFactory.AdminUsuario, "Pagamento lançado na conta errada"),
            (registro.Id, registro.Estornada, registro.EstornadaPor, registro.MotivoDoEstorno));
        Assert.NotNull(registro.EstornadaEm);

        var deNovo = await admin.PostAsync($"/api/financeiro/baixas/{baixa.Id}/estorno", new { motivo = "De novo" });
        Assert.Equal(HttpStatusCode.Conflict, deNovo.StatusCode);
        Assert.Equal("Este pagamento já foi estornado", (await deNovo.Content.ReadFromJsonAsync<ErroResposta>())!.Mensagem);

        var semMotivo = await admin.PostAsync($"/api/financeiro/baixas/{baixa.Id}/estorno", new { motivo = " " });
        Assert.Equal(HttpStatusCode.BadRequest, semMotivo.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.PostAsync($"/api/financeiro/baixas/{Guid.CreateVersion7()}/estorno", new { motivo = "x" })).StatusCode);
    }

    // ---- RN-23.7 (CA-5) ----

    [Fact]
    public async Task RN_23_7_conta_com_pagamento_so_edita_descricao_e_observacao_e_nao_e_excluida()
    {
        var admin = await AdminAsync();
        var omega = await FornecedorAsync(admin, "Imobiliária Ômega", Cnpj("232000000001"));
        var conta = await ContaAsync(admin, "Aluguel dezembro", "Aluguel", 2000m, "2026-12-10", omega.Documento, "Recibo 12");
        var baixa = (await (await PagarAsync(admin, conta.Id, 500m, "2026-11-05", await FormaAsync(admin, "Transferência")))
            .Content.ReadFromJsonAsync<ContaDto>())!.Baixas.Single();
        var aluguel = conta.CategoriaId;

        // Só descrição e observação: aceito.
        var permitida = await admin.PutAsync($"{Contas}/{conta.Id}", new
        {
            descricao = "Aluguel dez/26", categoriaId = aluguel, valor = 2000m, vencimento = "2026-12-10",
            fornecedorDocumento = conta.Fornecedor!.Documento, numeroDocumento = "Recibo 12", observacao = "Entrada paga",
        });
        Assert.Equal(HttpStatusCode.OK, permitida.StatusCode);
        var alterada = await ObterAsync(admin, conta.Id);
        Assert.Equal(("Aluguel dez/26", "Entrada paga", 1500m), (alterada.Descricao, alterada.Observacao, alterada.Saldo));

        // Categoria, fornecedor, documento, valor e vencimento travam.
        var energia = (await ListaAsync<CategoriaDto>(admin, Categorias)).Single(c => c.Nome == "Energia").Id;
        var travada = await admin.PutAsync($"{Contas}/{conta.Id}", new
        {
            descricao = "Aluguel dez/26", categoriaId = energia, valor = 2100m, vencimento = "2026-12-15",
            fornecedorDocumento = (string?)null, numeroDocumento = "Recibo 13", observacao = "Entrada paga",
        });
        Assert.Equal(HttpStatusCode.BadRequest, travada.StatusCode);
        var erros = JsonDocument.Parse(await travada.Content.ReadAsStringAsync()).RootElement.GetProperty("errors");
        foreach (var campo in new[] { "CategoriaId", "Valor", "Vencimento", "FornecedorDocumento", "NumeroDocumento" })
            Assert.Equal("Conta com pagamento: só a descrição e a observação podem ser alteradas", erros.GetProperty(campo)[0].GetString());
        Assert.Equal((2000m, new DateOnly(2026, 12, 10), omega.Id), ((await ObterAsync(admin, conta.Id)).Valor,
            (await ObterAsync(admin, conta.Id)).Vencimento, (await ObterAsync(admin, conta.Id)).Fornecedor!.Id));

        // Excluir conta com pagamento é recusado.
        var excluir = await admin.DeleteAsync($"{Contas}/{conta.Id}");
        Assert.Equal(HttpStatusCode.Conflict, excluir.StatusCode);
        Assert.Equal("Conta com pagamento não pode ser excluída. Estorne os pagamentos antes",
            (await excluir.Content.ReadFromJsonAsync<ErroResposta>())!.Mensagem);

        // Estornado o pagamento, a conta volta a ser editável e pode ser excluída (com a baixa estornada).
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/financeiro/baixas/{baixa.Id}/estorno", new { motivo = "Lançado errado" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{Contas}/{conta.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{Contas}/{conta.Id}")).StatusCode);
    }

    [Fact]
    public async Task Dois_pagamentos_ao_mesmo_tempo_nao_passam_do_saldo()
    {
        var admin = await AdminAsync();
        var conta = await ContaAsync(admin, "Imposto novembro", "Impostos", 300m);
        var dinheiro = await FormaAsync(admin, "Dinheiro");
        await using var primeiro = factory.Services.CreateAsyncScope();
        await using var segundo = factory.Services.CreateAsyncScope();
        var repositorio1 = primeiro.ServiceProvider.GetRequiredService<IContasRepository>();
        var repositorio2 = segundo.ServiceProvider.GetRequiredService<IContasRepository>();

        // As duas operações leem a conta com saldo R$ 300,00 antes de qualquer uma gravar.
        var lida1 = (await repositorio1.ObterAsync(conta.Id, CancellationToken.None))!;
        var lida2 = (await repositorio2.ObterAsync(conta.Id, CancellationToken.None))!;
        lida1.Pagar(new DateOnly(2026, 11, 5), 300m, dinheiro, DateTimeOffset.UtcNow);
        lida2.Pagar(new DateOnly(2026, 11, 5), 300m, dinheiro, DateTimeOffset.UtcNow);

        Assert.True(await repositorio1.SalvarAsync(CancellationToken.None));
        Assert.False(await repositorio2.SalvarAsync(CancellationToken.None)); // a versão da linha mudou

        var final = await ObterAsync(admin, conta.Id);
        Assert.Equal((0m, 300m, 1), (final.Saldo, final.ValorPago, final.Baixas.Count));
    }

    // ---- RN-23.4 (CA-3) ----

    [Fact]
    public async Task RN_23_4_formas_de_pagamento_financeiras_cadastradas()
    {
        var admin = await AdminAsync();
        var nomes = (await ListaAsync<FormaDePagamentoDto>(admin, $"{Formas}?incluirInativas=true")).Select(f => f.Nome).ToHashSet();
        Assert.Subset(nomes, new HashSet<string> { "Dinheiro", "Pix", "Boleto", "Transferência" });

        var criada = await admin.PostAsync(Formas, new { nome = "Cartão corporativo" });
        Assert.Equal(HttpStatusCode.Created, criada.StatusCode);
        var cartao = (await criada.Content.ReadFromJsonAsync<FormaDePagamentoDto>())!;
        var repetida = await admin.PostAsync(Formas, new { nome = "pix" });
        Assert.Equal(HttpStatusCode.Conflict, repetida.StatusCode);
        Assert.Equal("Já existe a forma de pagamento Pix", (await repetida.Content.ReadFromJsonAsync<ErroResposta>())!.Mensagem);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync(Formas, new { nome = " " })).StatusCode);

        // Com pagamento, a forma não é excluída, só inativada; inativa, não aceita pagamento novo.
        var conta = await ContaAsync(admin, "Manutenção do site", "Internet", 90m);
        Assert.Equal(HttpStatusCode.Created, (await PagarAsync(admin, conta.Id, 40m, "2026-11-05", cartao.Id)).StatusCode);
        var excluir = await admin.DeleteAsync($"{Formas}/{cartao.Id}");
        Assert.Equal(HttpStatusCode.Conflict, excluir.StatusCode);
        Assert.Equal("Esta forma de pagamento tem pagamentos. Você pode inativá-la", (await excluir.Content.ReadFromJsonAsync<ErroResposta>())!.Mensagem);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"{Formas}/{cartao.Id}/inativar")).StatusCode);
        Assert.DoesNotContain(await ListaAsync<FormaDePagamentoDto>(admin, Formas), f => f.Id == cartao.Id);
        var naInativa = await PagarAsync(admin, conta.Id, 10m, "2026-11-06", cartao.Id);
        var erro = JsonDocument.Parse(await naInativa.Content.ReadAsStringAsync()).RootElement.GetProperty("errors");
        Assert.Equal("Forma de pagamento inativa", erro.GetProperty("FormaDePagamentoId")[0].GetString());
        // A baixa continua mostrando o nome da forma inativa.
        Assert.Equal("Cartão corporativo", (await ObterAsync(admin, conta.Id)).Baixas.Single().FormaDePagamento);

        var temporaria = (await (await admin.PostAsync(Formas, new { nome = "Cheque" })).Content.ReadFromJsonAsync<FormaDePagamentoDto>())!;
        var renomeada = await admin.PutAsync($"{Formas}/{temporaria.Id}", new { nome = "Cheque nominal" });
        Assert.Equal("Cheque nominal", (await renomeada.Content.ReadFromJsonAsync<FormaDePagamentoDto>())!.Nome);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{Formas}/{temporaria.Id}")).StatusCode);
    }

    [Fact]
    public async Task RN_23_4_nome_da_forma_unico_sem_diferenciar_maiusculas_tambem_no_banco()
    {
        _ = factory.CriarCliente(); // Host no ar: migrações e seed ("Pix" já existe)
        await using var escopo = factory.Services.CreateAsyncScope();
        var repositorio = escopo.ServiceProvider.GetRequiredService<IFormasDePagamentoRepository>();

        Assert.False(await repositorio.AdicionarAsync(
            Impa.Modules.Financeiro.Domain.FormaDePagamentoFinanceira.Criar("PIX", DateTimeOffset.UtcNow), CancellationToken.None));
    }

    // ---- Permissões: pagar e estornar com financeiro.editar (decisão de 07/11/2026) ----

    [Fact]
    public async Task Pagar_estornar_e_cadastrar_formas_so_com_financeiro_editar()
    {
        var admin = await AdminAsync();
        Assert.Equal(HttpStatusCode.Created,
            (await admin.PostAsync("/api/perfis", new { nome = "Consulta de pagamentos", permissoes = new[] { Permissoes.FinanceiroVer } })).StatusCode);
        await factory.CriarUsuarioAsync("marta", "consulta-2026", "Marta", "Consulta de pagamentos");
        var marta = await EntrarAsync("marta", "consulta-2026");
        var conta = await ContaAsync(admin, "Energia novembro", "Energia", 280m);
        var pix = await FormaAsync(admin, "Pix");
        var baixa = (await (await PagarAsync(admin, conta.Id, 280m, "2026-11-05", pix)).Content.ReadFromJsonAsync<ContaDto>())!.Baixas.Single();

        Assert.Equal(HttpStatusCode.OK, (await marta.GetAsync(Formas)).StatusCode);
        Assert.Equal("paga", (await ObterAsync(marta, conta.Id)).SituacaoDoPagamento);
        Assert.Equal(HttpStatusCode.Forbidden, (await PagarAsync(marta, conta.Id, 1m, "2026-11-05", pix)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await marta.PostAsync($"/api/financeiro/baixas/{baixa.Id}/estorno", new { motivo = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await marta.PostAsync(Formas, new { nome = "Vale" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await marta.DeleteAsync($"{Formas}/{pix}")).StatusCode);
    }

    private static decimal Dec(string valor) => decimal.Parse(valor, System.Globalization.CultureInfo.InvariantCulture);
}
