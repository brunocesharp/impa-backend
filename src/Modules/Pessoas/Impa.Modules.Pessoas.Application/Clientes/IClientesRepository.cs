using Impa.Modules.Pessoas.Application.Comum;
using Impa.Modules.Pessoas.Domain;
using Impa.Shared;

namespace Impa.Modules.Pessoas.Application.Clientes;

/// <summary>Cliente na tela (RN-14.6: só para quem tem <c>clientes.ver</c>). Documento com máscara; telefone só dígitos.</summary>
public sealed record ClienteDto(
    Guid Id,
    string Nome,
    string? Documento,
    string? Telefone,
    string? Email,
    string? Endereco,
    string? Observacoes,
    bool Ativo)
{
    public static ClienteDto De(Cliente c) => new(c.Id, c.Nome,
        c.Documento is null ? null : Impa.Shared.Documento.Criar(c.Documento).Formatado,
        c.Telefone, c.Email, c.Endereco, c.Observacoes, c.Ativo);

    // RN-14.6: o ToString padrão do record levaria CPF, telefone, e-mail e endereço para qualquer log.
    public override string ToString() => $"{nameof(ClienteDto)} {{ Id = {Id} }}";
}

/// <summary>Persistência dos clientes (schema <c>pessoas</c>).</summary>
public interface IClientesRepository
{
    Task<Cliente?> ObterAsync(Guid id, CancellationToken ct);

    /// <summary>Outro cliente com o mesmo documento (só dígitos), ativo ou não; ignora <paramref name="exceto"/>.</summary>
    Task<Cliente?> ObterPorDocumentoAsync(string documento, Guid? exceto, CancellationToken ct);

    Task<IReadOnlyList<Cliente>> BuscarAsync(TermosDeBusca termos, CancellationToken ct);

    /// <summary>Grava o cliente novo. Falso quando o índice único do documento recusou (cadastro simultâneo).</summary>
    Task<bool> AdicionarAsync(Cliente cliente, CancellationToken ct);

    /// <summary>Grava as alterações. Falso quando o índice único do documento recusou.</summary>
    Task<bool> SalvarAsync(CancellationToken ct);

    Task RemoverAsync(Cliente cliente, CancellationToken ct);
}
