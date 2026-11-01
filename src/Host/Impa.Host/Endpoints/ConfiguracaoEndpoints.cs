using Impa.Host.Seguranca;
using Impa.Modules.Acesso.Contracts;

namespace Impa.Host.Endpoints;

public sealed record EmpresaDto(string Nome);

/// <summary>
/// Exemplo mínimo de endpoint protegido por permissão (story 1-1, T4). O módulo de parâmetros
/// (story 4-1) assume esta rota; até lá o nome vem de <c>Empresa:Nome</c> na configuração.
/// </summary>
public static class ConfiguracaoEndpoints
{
    public static IEndpointRouteBuilder MapConfiguracaoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/configuracao/empresa", (IConfiguration configuracao) =>
                Results.Ok(new EmpresaDto(configuracao["Empresa:Nome"] ?? string.Empty)))
            .RequirePermissao(Permissoes.ConfiguracaoVer)
            .WithTags("Configuração")
            .WithName("ObterEmpresa")
            .WithSummary("Nome da empresa (exige configuracao.ver)")
            .Produces<EmpresaDto>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return app;
    }
}
