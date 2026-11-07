using Ardalis.Result;

namespace Impa.Modules.Acesso.Application.Usuarios;

public static class MensagensUsuarios
{
    /// <summary>Cenário 1.6 / RN-1.6.</summary>
    public const string UltimoAdministrador = "É preciso manter pelo menos um administrador ativo";

    public const string NaoEncontrado = "Usuário não encontrado";

    public const string LoginEmUso = "Já existe um usuário com esse login";
}

/// <summary>Primitivas de gestão de usuários (implementadas na Infrastructure com o Identity).</summary>
public interface IGestaoUsuarios
{
    Task<bool> ExisteAsync(Guid usuarioId, CancellationToken ct);

    /// <summary>Usuário ativo com o perfil protegido (Administrador).</summary>
    Task<bool> EhAdministradorAtivoAsync(Guid usuarioId, CancellationToken ct);

    Task<int> ContarAdministradoresAtivosAsync(CancellationToken ct);

    /// <summary>
    /// Roda <paramref name="operacao"/> numa transação com uma trava exclusiva sobre "quem é administrador ativo":
    /// duas alterações simultâneas (ex.: dois administradores se desativando) não deixam a loja sem nenhum.
    /// Confirma só se o resultado for sucesso.
    /// </summary>
    Task<Result> ComAdministradoresTravadosAsync(Func<Task<Result>> operacao, CancellationToken ct);

    /// <summary>
    /// Marca o usuário como inativo, revoga todas as sessões abertas dele e troca o security stamp,
    /// numa transação (RN-1.7). Idempotente.
    /// </summary>
    Task DesativarAsync(Guid usuarioId, CancellationToken ct);

    /// <summary>Deixa o usuário com exatamente o perfil indicado (um perfil por usuário, RN-1.4).</summary>
    Task TrocarPerfilAsync(Guid usuarioId, Guid perfilId, CancellationToken ct);
}
