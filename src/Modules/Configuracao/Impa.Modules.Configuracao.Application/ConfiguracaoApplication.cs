using System.Reflection;

namespace Impa.Modules.Configuracao.Application;

/// <summary>Marcador do assembly: o Host registra daqui os handlers (MediatR) e os validators (FluentValidation).</summary>
public static class ConfiguracaoApplication
{
    public static Assembly Assembly { get; } = typeof(ConfiguracaoApplication).Assembly;
}
