using System.Reflection;

namespace Impa.Host.Endpoints;

/// <param name="Versao">Versão semântica (<c>Version</c> do <c>Directory.Build.props</c>).</param>
/// <param name="Commit">Commit curto do git que gerou o build, ou <c>dev</c> quando não se sabe.</param>
public sealed record VersaoDto(string Versao, string Commit)
{
    public const string CommitDesconhecido = "dev";

    /// <summary>
    /// Lê o <c>AssemblyInformationalVersion</c> (<c>0.1.0+{hash}</c>): o SDK acrescenta o hash do git
    /// automaticamente; no Docker, onde não há <c>.git</c>, ele vem de <c>-p:SourceRevisionId</c>.
    /// </summary>
    public static VersaoDto Da(Assembly assembly)
    {
        var informacional = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";
        return DoTexto(informacional);
    }

    /// <summary><c>0.1.0+1f55d46c...</c> → (<c>0.1.0</c>, <c>1f55d46</c>); sem <c>+hash</c> → commit <c>dev</c>.</summary>
    public static VersaoDto DoTexto(string informacional)
    {
        var partes = informacional.Split('+', 2);
        var commit = partes.Length == 2 && partes[1].Length >= 7 ? partes[1][..7].ToLowerInvariant() : CommitDesconhecido;
        return new VersaoDto(partes[0], commit);
    }
}

/// <summary><c>GET /api/versao</c>: versão da API para o rodapé do sistema.</summary>
public static class VersaoEndpoints
{
    private static readonly VersaoDto Atual = VersaoDto.Da(typeof(VersaoEndpoints).Assembly);

    public static IEndpointRouteBuilder MapVersaoEndpoints(this IEndpointRouteBuilder app)
    {
        // Exige sessão (política padrão): a versão só aparece para quem está dentro do sistema.
        app.MapGet("/api/versao", () => Results.Ok(Atual))
            .WithTags("Sistema")
            .WithName("ObterVersao")
            .WithSummary("Versão e commit da API")
            .Produces<VersaoDto>()
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }
}
