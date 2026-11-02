using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Impa.Modules.Acesso.Infrastructure.Identity;

/// <summary>
/// Hash de uma senha aleatória, calculado uma vez com o mesmo hasher do Identity (mesmo algoritmo e iterações),
/// para o login de usuário inexistente gastar o mesmo tempo de uma senha errada. Preparado na inicialização
/// (<see cref="DependencyInjection.PrepararAcesso"/>), antes da primeira requisição: senão a primeira tentativa
/// com login inexistente pagaria dois PBKDF2 e o tempo revelaria que o login não existe.
/// </summary>
public sealed class HashFicticio
{
    private string? _valor;

    public string Obter(IPasswordHasher<Usuario> hasher) =>
        LazyInitializer.EnsureInitialized(
            ref _valor,
            () => hasher.HashPassword(new Usuario(), Guid.NewGuid().ToString("N")));

    internal void Preparar(IServiceProvider servicos)
    {
        using var escopo = servicos.CreateScope();
        Obter(escopo.ServiceProvider.GetRequiredService<IPasswordHasher<Usuario>>());
    }
}
