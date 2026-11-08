using Impa.Modules.Configuracao.Application.Auditoria.Queries.ConsultarAuditoria;
using Impa.Modules.Configuracao.Domain.Auditoria;

namespace Impa.Modules.Configuracao.Application.Auditoria;

/// <summary>Um campo do registro: <paramref name="Omitido"/> = sensível, sem os valores.</summary>
public sealed record CampoAuditadoDto(string Campo, string? Antes, string? Depois, bool Omitido);

/// <summary>Uma operação sensível consultada: usuário, data, operação e os valores antes e depois (CA-1).</summary>
public sealed record RegistroDeAuditoriaDto(
    Guid Id,
    DateTimeOffset OcorreuEm,
    Guid UsuarioId,
    string UsuarioNome,
    string Modulo,
    string Entidade,
    string EntidadeId,
    string Operacao,
    IReadOnlyList<CampoAuditadoDto> Campos);

/// <summary>Página da consulta, mais recentes primeiro. <paramref name="Total"/>: todos os que atendem aos filtros.</summary>
public sealed record PaginaDeAuditoriaDto(IReadOnlyList<RegistroDeAuditoriaDto> Itens, int Pagina, int TamanhoDaPagina, int Total);

/// <summary>Registros de auditoria do módulo Configuração (tabela <c>configuracao.auditoria</c>); implementado na Infrastructure.</summary>
public interface IRegistrosDeAuditoria
{
    /// <summary>Só adiciona: o despachante grava junto com o Inbox (idempotência, ADR-011).</summary>
    void Adicionar(RegistroDeAuditoria registro);

    Task<PaginaDeAuditoriaDto> ConsultarAsync(ConsultarAuditoriaQuery consulta, CancellationToken ct);
}
