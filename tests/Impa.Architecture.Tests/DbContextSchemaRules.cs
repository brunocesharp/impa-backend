using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Impa.Architecture.Tests;

/// <summary>ADR-012, regra 1: o contexto de um módulo só mapeia tabelas, views e sequências do schema do módulo.</summary>
public static class DbContextSchemaRules
{
    private const string ModulePrefix = "Impa.Modules.";

    /// <summary><c>Impa.Modules.Acesso.Infrastructure</c> → <c>acesso</c>.</summary>
    public static string SchemaDoModulo(string assemblyName) =>
        assemblyName[ModulePrefix.Length..].Split('.')[0].ToLowerInvariant();

    public static IReadOnlyList<string> Violations(Type tipoDoContexto, string schemaEsperado)
    {
        using var contexto = Criar(tipoDoContexto);
        var modelo = contexto.GetService<IDesignTimeModel>().Model;
        var violacoes = new List<string>();

        foreach (var entidade in modelo.GetEntityTypes())
        {
            var (schema, nome) = entidade.GetTableName() is { } tabela
                ? (entidade.GetSchema(), tabela)
                : (entidade.GetViewSchema(), entidade.GetViewName());
            if (nome is not null && schema != schemaEsperado)
                violacoes.Add($"{tipoDoContexto.Name} mapeia {schema ?? "(sem schema)"}.{nome} fora do schema '{schemaEsperado}'");
        }

        foreach (var sequencia in modelo.GetSequences())
        {
            if (sequencia.Schema != schemaEsperado)
                violacoes.Add($"{tipoDoContexto.Name} mapeia a sequência {sequencia.Schema}.{sequencia.Name} fora do schema '{schemaEsperado}'");
        }

        return violacoes;
    }

    /// <summary>Instancia o contexto só para ler o modelo (o Npgsql não conecta para isso).</summary>
    private static DbContext Criar(Type tipoDoContexto)
    {
        var builder = (DbContextOptionsBuilder)Activator.CreateInstance(
            typeof(DbContextOptionsBuilder<>).MakeGenericType(tipoDoContexto))!;
        builder.UseNpgsql("Host=localhost;Database=modelo");
        return (DbContext)Activator.CreateInstance(tipoDoContexto, builder.Options)!;
    }
}
