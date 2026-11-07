using Impa.Host.Http;
using Microsoft.AspNetCore.Authorization;

namespace Impa.Host.Seguranca;

/// <summary>Metadado do endpoint liberado para quem está com a senha provisória (<see cref="TrocaDeSenhaObrigatoria"/>).</summary>
public sealed class LiberadoNaTrocaDeSenhaMetadata;

/// <summary>
/// RN-1.8 (story 1.4, T3): com a senha provisória, o usuário só troca a senha. Até lá, todo endpoint que exige sessão
/// responde 403 <c>{ mensagem, codigo: "troca_de_senha_obrigatoria" }</c>, exceto os marcados com
/// <see cref="LiberarNaTrocaDeSenha{TBuilder}"/> (trocar a senha, consultar a sessão e sair).
/// </summary>
public static class TrocaDeSenhaObrigatoria
{
    public const string Codigo = "troca_de_senha_obrigatoria";
    public const string Mensagem = "Defina uma nova senha para continuar";

    /// <summary>Endpoint que continua acessível enquanto a troca de senha estiver pendente.</summary>
    public static TBuilder LiberarNaTrocaDeSenha<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new LiberadoNaTrocaDeSenhaMetadata());

    /// <summary>Depois de <c>UsePermissoesDaSessao</c> (de onde vem a marca) e antes da autorização.</summary>
    public static IApplicationBuilder UseTrocaDeSenhaObrigatoria(this IApplicationBuilder app) =>
        app.Use(async (contexto, proximo) =>
        {
            if (contexto.PermissoesDaSessao().TrocaDeSenhaObrigatoria && Bloqueia(contexto.GetEndpoint()))
            {
                contexto.Response.StatusCode = StatusCodes.Status403Forbidden;
                await contexto.Response.WriteAsJsonAsync(new ErroDto(Mensagem, Codigo));
                return;
            }

            await proximo(contexto);
        });

    // Rota inexistente segue para o 404; endpoints anônimos (versão, health) não são "funções" do sistema.
    private static bool Bloqueia(Endpoint? endpoint) =>
        endpoint is not null
        && endpoint.Metadata.GetMetadata<LiberadoNaTrocaDeSenhaMetadata>() is null
        && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null;
}
