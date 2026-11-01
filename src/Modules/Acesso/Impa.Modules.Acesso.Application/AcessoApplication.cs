using System.Reflection;

namespace Impa.Modules.Acesso.Application;

/// <summary>Marcador do assembly: o Host registra daqui os handlers (MediatR) e os validators (FluentValidation).</summary>
public static class AcessoApplication
{
    public static Assembly Assembly { get; } = typeof(AcessoApplication).Assembly;
}
