using Impa.Host.Endpoints;
using Impa.Host.Seguranca;
using Impa.Modules.Acesso.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Impa.Api.IntegrationTests;

/// <summary>Item com custo, como o do cadastro de itens (épico 3): o custo só sai para quem tem <c>catalogo.custo.ver</c>.</summary>
public sealed record ItemDeExemplo(
    string Descricao,
    decimal Preco,
    [property: ExigePermissao(Permissoes.CatalogoCustoVer)] decimal Custo);

/// <summary>
/// Funções do cenário 1.5 que ainda não têm endpoint (PDV, contas a pagar, fiscal, importação, itens), protegidas
/// com as permissões reais do catálogo — a mesma convenção que os endpoints de verdade vão usar (README).
/// </summary>
public sealed class EndpointsDeExemplo : IEndpointsAdicionais
{
    public const string Pdv = "/api/teste/pdv/vender";
    public const string ContasAPagar = "/api/teste/financeiro/contas-a-pagar";
    public const string ConfiguracaoFiscal = "/api/teste/configuracao/fiscal";
    public const string Importacao = "/api/teste/importacao";
    public const string Item = "/api/teste/catalogo/itens/1";

    /// <summary>PDV e caixa só em terminal registrado (story 1-6): os endpoints reais chegam nos épicos 5 e 6.</summary>
    public const string PdvNoTerminal = "/api/teste/terminal/pdv/vender";
    public const string CaixaNoTerminal = "/api/teste/terminal/caixa";

    public void Mapear(IEndpointRouteBuilder app)
    {
        app.MapPost(Pdv, () => Results.NoContent()).RequirePermissao(Permissoes.PdvVender);
        app.MapGet(ContasAPagar, () => Results.Ok(Array.Empty<object>())).RequirePermissao(Permissoes.FinanceiroVer);
        app.MapPut(ConfiguracaoFiscal, () => Results.NoContent()).RequirePermissao(Permissoes.ConfiguracaoFiscal);
        app.MapPost(Importacao, () => Results.NoContent()).RequirePermissao(Permissoes.ImportacaoExecutar);
        app.MapGet(Item, () => Results.Ok(new ItemDeExemplo("Caderno 10 matérias", 32.90m, 18.40m)))
            .RequirePermissao(Permissoes.CatalogoVer);
        app.MapPost(PdvNoTerminal, () => Results.NoContent()).RequirePermissao(Permissoes.PdvVender).RequireTerminal();
        app.MapGet(CaixaNoTerminal, () => Results.Ok(Array.Empty<object>())).RequirePermissao(Permissoes.CaixaOperar).RequireTerminal();
    }
}
