using System.Reflection;

namespace Impa.Modules.Pessoas.Application;

/// <summary>Marcador do assembly: o Host registra daqui os handlers (MediatR) e os validators (FluentValidation).</summary>
public static class PessoasApplication
{
    public static Assembly Assembly { get; } = typeof(PessoasApplication).Assembly;
}
