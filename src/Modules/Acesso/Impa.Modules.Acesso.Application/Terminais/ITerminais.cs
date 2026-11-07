namespace Impa.Modules.Acesso.Application.Terminais;

/// <summary>
/// Terminal na lista da tela Configuração → Terminais. <paramref name="Atual"/>: é o terminal deste dispositivo
/// (o cookie do terminal da requisição). <paramref name="CriadoPor"/>: nome de quem registrou.
/// </summary>
public sealed record TerminalDto(
    Guid Id,
    string Nome,
    string CriadoPor,
    DateTimeOffset CriadoEm,
    DateTimeOffset? RevogadoEm,
    bool Atual);

/// <summary>Terminal válido (registrado e não revogado) do dispositivo da requisição.</summary>
public sealed record TerminalAtualDto(Guid Id, string Nome);

public static class MensagensTerminais
{
    public const string NaoEncontrado = "Terminal não encontrado";
    public const string NomeEmUso = "Já existe um terminal com esse nome";
    public const string Revogado = "O terminal foi revogado e não pode ser alterado";

    /// <summary>RN-11.1 / cenário 11.6: texto da tela e do 403 <c>terminal_nao_registrado</c>.</summary>
    public const string NaoRegistrado = "Este computador não está registrado como terminal. Peça ao administrador";

    public static string DispositivoJaRegistrado(string nome) => $"Este dispositivo já está registrado como o terminal \"{nome}\"";
}

/// <summary>
/// Primitivas de terminais (implementadas na Infrastructure com o <c>AcessoDbContext</c>). As regras — nome único
/// entre os terminais ativos, um registro por dispositivo, revogado não muda — ficam nos handlers.
/// O token do terminal nunca chega aqui: só o hash (<see cref="TokenDoTerminal.Hash"/>).
/// </summary>
public interface ITerminais
{
    /// <summary>Todos os terminais, ativos primeiro e por nome; <paramref name="hashAtual"/> marca o deste dispositivo.</summary>
    Task<IReadOnlyList<TerminalDto>> ListarAsync(byte[]? hashAtual, CancellationToken ct);

    Task<TerminalDto?> ObterAsync(Guid id, CancellationToken ct);

    /// <summary>Terminal registrado e não revogado com esse hash de token; <c>null</c> se não houver.</summary>
    Task<TerminalAtualDto?> ObterValidoAsync(byte[] tokenHash, CancellationToken ct);

    /// <summary>Nome já usado por outro terminal ativo (sem diferenciar maiúsculas), exceto <paramref name="exceto"/>.</summary>
    Task<bool> NomeEmUsoAsync(string nome, Guid? exceto, CancellationToken ct);

    /// <exception cref="NomeDeTerminalEmUsoException">outro terminal ativo ficou com o nome no meio do caminho.</exception>
    Task<TerminalDto> RegistrarAsync(string nome, Guid criadoPor, byte[] tokenHash, CancellationToken ct);

    /// <exception cref="NomeDeTerminalEmUsoException">outro terminal ativo ficou com o nome no meio do caminho.</exception>
    Task<TerminalDto> RenomearAsync(Guid id, string nome, CancellationToken ct);

    /// <summary>Marca a revogação (a primeira data fica). Vale na próxima requisição do dispositivo.</summary>
    Task RevogarAsync(Guid id, CancellationToken ct);
}

/// <summary>Violação do índice único de nome na gravação (corrida entre dois registros com o mesmo nome).</summary>
public sealed class NomeDeTerminalEmUsoException(Exception interna) : Exception(MensagensTerminais.NomeEmUso, interna);
