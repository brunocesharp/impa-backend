using System.Reflection;

namespace Impa.Architecture.Tests;

public class ModuleDependencyTests
{
    [Theory]
    [InlineData("Impa.Modules.Vendas.Application", "Impa.Modules.Catalogo.Contracts")]
    [InlineData("Impa.Modules.Vendas.Infrastructure", "Impa.Modules.Vendas.Application")]
    [InlineData("Impa.Modules.Vendas.Application", "Impa.Shared")]
    public void Dependencias_permitidas_nao_geram_violacao(string assembly, string reference)
    {
        Assert.Empty(ModuleDependencyRules.Violations(assembly, [reference]));
    }

    [Theory]
    [InlineData("Impa.Modules.Vendas.Application", "Impa.Modules.Catalogo.Domain")]
    [InlineData("Impa.Modules.Vendas.Infrastructure", "Impa.Modules.Catalogo.Infrastructure")]
    [InlineData("Impa.Modules.Vendas.Contracts", "Impa.Modules.Catalogo.Application")]
    [InlineData("Impa.Modules.Vendas.Domain", "Impa.Modules.Vendas.Infrastructure")]
    [InlineData("Impa.Modules.Vendas.Application", "Impa.Modules.Vendas.Infrastructure")]
    [InlineData("Impa.Modules.Vendas.Contracts", "Impa.Modules.Vendas.Domain")]
    public void Dependencias_proibidas_geram_violacao(string assembly, string reference)
    {
        Assert.NotEmpty(ModuleDependencyRules.Violations(assembly, [reference]));
    }

    [Fact]
    public void Modulos_da_solucao_so_se_referenciam_pelos_contratos()
    {
        var moduleAssemblies = Directory
            .GetFiles(AppContext.BaseDirectory, "Impa.Modules.*.dll")
            .Select(path => Assembly.LoadFrom(path))
            .ToList();

        Assert.Contains(moduleAssemblies, a => a.GetName().Name == "Impa.Modules.Acesso.Domain");

        var violations = moduleAssemblies
            .SelectMany(a => ModuleDependencyRules.Violations(
                a.GetName().Name!,
                a.GetReferencedAssemblies().Select(r => r.Name!)))
            .ToList();

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }
}
