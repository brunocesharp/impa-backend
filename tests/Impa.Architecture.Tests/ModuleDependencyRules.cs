namespace Impa.Architecture.Tests;

/// <summary>
/// Regras de dependência do monólito modular (ADR-001 / ADR-012):
/// um módulo só referencia outro pelo projeto <c>Impa.Modules.{Outro}.Contracts</c>;
/// dentro do módulo, Domain e Contracts não dependem das camadas de fora.
/// </summary>
public static class ModuleDependencyRules
{
    private const string ModulePrefix = "Impa.Modules.";

    private static readonly Dictionary<string, string[]> ForbiddenSameModuleLayers = new()
    {
        ["Domain"] = ["Application", "Infrastructure", "Contracts"],
        ["Contracts"] = ["Domain", "Application", "Infrastructure"],
        ["Application"] = ["Infrastructure"],
    };

    public static IReadOnlyList<string> Violations(string assemblyName, IEnumerable<string> referencedAssemblyNames)
    {
        if (!TryParse(assemblyName, out var module, out var layer))
            return [];

        var violations = new List<string>();
        foreach (var reference in referencedAssemblyNames)
        {
            if (!TryParse(reference, out var refModule, out var refLayer))
                continue;

            if (refModule != module && refLayer != "Contracts")
                violations.Add($"{assemblyName} referencia {reference}: outro módulo só pelo projeto Contracts");

            if (refModule == module
                && ForbiddenSameModuleLayers.TryGetValue(layer, out var forbidden)
                && forbidden.Contains(refLayer))
                violations.Add($"{assemblyName} referencia {reference}: camada {layer} não pode depender de {refLayer}");
        }

        return violations;
    }

    private static bool TryParse(string assemblyName, out string module, out string layer)
    {
        module = layer = string.Empty;
        if (!assemblyName.StartsWith(ModulePrefix, StringComparison.Ordinal))
            return false;

        var parts = assemblyName[ModulePrefix.Length..].Split('.');
        if (parts.Length < 2)
            return false;

        module = parts[0];
        layer = parts[1];
        return true;
    }
}
