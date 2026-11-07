using System.Collections.Concurrent;
using Impa.Modules.Acesso.Application.Autenticacao;
using Impa.Modules.Acesso.Contracts;
using Impa.Modules.Acesso.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Impa.Modules.Acesso.Infrastructure.Identity;

/// <summary>
/// Cache em memória (singleton) das permissões por usuário. Cada entrada leva a geração em que a leitura começou;
/// <see cref="Invalidar"/> troca a geração e descarta tudo, então uma leitura que cruzou a invalidação não é guardada.
/// Vale para uma instância da API (o deploy atual); com várias, a invalidação teria de ser distribuída.
/// </summary>
public sealed class CacheDePermissoes(TimeProvider relogio)
{
    /// <summary>Validade máxima de uma entrada, mesmo sem invalidação (mudança feita direto no banco).</summary>
    public static readonly TimeSpan Validade = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<Guid, Entrada> _itens = new();
    private long _geracao;

    private sealed record Entrada(long Geracao, DateTimeOffset ExpiraEm, PermissoesCarregadas Valor);

    public long Geracao => Interlocked.Read(ref _geracao);

    public bool TentarObter(Guid usuarioId, out PermissoesCarregadas valor)
    {
        if (_itens.TryGetValue(usuarioId, out var entrada) && entrada.Geracao == Geracao && entrada.ExpiraEm > relogio.GetUtcNow())
        {
            valor = entrada.Valor;
            return true;
        }

        valor = PermissoesCarregadas.Nenhuma;
        return false;
    }

    public void Guardar(Guid usuarioId, long geracaoDaLeitura, PermissoesCarregadas valor)
    {
        if (geracaoDaLeitura == Geracao)
            _itens[usuarioId] = new Entrada(geracaoDaLeitura, relogio.GetUtcNow().Add(Validade), valor);
    }

    public void Invalidar()
    {
        Interlocked.Increment(ref _geracao);
        _itens.Clear();
    }
}

/// <summary>Permissões do perfil atual do usuário, do banco, com <see cref="CacheDePermissoes"/>.</summary>
public sealed class PermissoesDoUsuario(AcessoDbContext banco, CacheDePermissoes cache) : IPermissoesDoUsuario
{
    public async Task<PermissoesCarregadas> ObterAsync(Guid usuarioId, CancellationToken ct)
    {
        var geracao = cache.Geracao;
        if (cache.TentarObter(usuarioId, out var emCache))
            return emCache;

        var perfil = await (
                from ur in banco.UserRoles
                join u in banco.Users on ur.UserId equals u.Id
                join r in banco.Roles on ur.RoleId equals r.Id
                where ur.UserId == usuarioId && u.Ativo
                orderby r.Name
                select new { r.Id, r.Name })
            .FirstOrDefaultAsync(ct);

        var carregadas = PermissoesCarregadas.Nenhuma;
        if (perfil is not null)
        {
            var codigos = await banco.RoleClaims
                .Where(c => c.RoleId == perfil.Id && c.ClaimType == Permissoes.ClaimType)
                .Select(c => c.ClaimValue!)
                .ToListAsync(ct);

            // Só o que está no catálogo: uma claim antiga ou digitada no banco não vira permissão.
            carregadas = new PermissoesCarregadas(perfil.Name ?? string.Empty,
                codigos.Where(Permissoes.Existe).ToHashSet(StringComparer.Ordinal));
        }

        cache.Guardar(usuarioId, geracao, carregadas);
        return carregadas;
    }

    public void Invalidar() => cache.Invalidar();
}
