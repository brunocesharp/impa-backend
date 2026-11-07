using System.Text.Json.Serialization.Metadata;
using Impa.Modules.Acesso.Contracts;
using Microsoft.AspNetCore.Http.Json;

namespace Impa.Host.Seguranca;

/// <summary>
/// Convenção da story 1.3 (CA-1, decisão de 07/11/2026): propriedade marcada com <see cref="ExigePermissaoAttribute"/>
/// é omitida do JSON quando o usuário da requisição não tem a permissão (ex.: custo para o Operador de caixa).
/// </summary>
public static class CamposPorPermissao
{
    public static IServiceCollection AddCamposPorPermissao(this IServiceCollection services)
    {
        services.AddOptions<JsonOptions>().Configure<IHttpContextAccessor>((opcoes, acessor) =>
        {
            var base_ = opcoes.SerializerOptions.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver();
            opcoes.SerializerOptions.TypeInfoResolver = base_.WithAddedModifier(info => Modificar(info, acessor));
        });

        return services;
    }

    private static void Modificar(JsonTypeInfo info, IHttpContextAccessor acessor)
    {
        if (info.Kind != JsonTypeInfoKind.Object)
            return;

        foreach (var propriedade in info.Properties)
        {
            var exige = propriedade.AttributeProvider?
                .GetCustomAttributes(typeof(ExigePermissaoAttribute), inherit: true)
                .OfType<ExigePermissaoAttribute>()
                .FirstOrDefault();
            if (exige is null)
                continue;

            var anterior = propriedade.ShouldSerialize; // mantém, por exemplo, o JsonIgnore(WhenWritingNull)
            var permissao = exige.Permissao;
            // Fora de uma requisição (sem HttpContext) o campo também não sai: negar por padrão.
            propriedade.ShouldSerialize = (objeto, valor) =>
                acessor.HttpContext?.PermissoesDaSessao().Tem(permissao) == true
                && (anterior is null || anterior(objeto, valor));
        }
    }
}
