using Impa.Modules.Configuracao.Application.Auditoria;
using Impa.Modules.Configuracao.Application.Auditoria.Queries.ConsultarAuditoria;
using Impa.Modules.Configuracao.Domain.Auditoria;
using Impa.Modules.Configuracao.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Impa.Modules.Configuracao.Infrastructure.Auditoria;

public sealed class RepositorioDeAuditoria(ConfiguracaoDbContext banco) : IRegistrosDeAuditoria
{
    public void Adicionar(RegistroDeAuditoria registro) => banco.Auditoria.Add(registro);

    public async Task<PaginaDeAuditoriaDto> ConsultarAsync(ConsultarAuditoriaQuery consulta, CancellationToken ct)
    {
        var registros = banco.Auditoria.AsNoTracking();
        if (consulta.De is { } de)
            registros = registros.Where(r => r.OcorreuEm >= de.ToUniversalTime());
        if (consulta.Ate is { } ate)
            registros = registros.Where(r => r.OcorreuEm < ate.ToUniversalTime());
        if (Trecho(consulta.Usuario) is { } usuario)
            registros = registros.Where(r => EF.Functions.ILike(r.UsuarioNome, usuario, "\\"));
        if (Trecho(consulta.Modulo) is { } modulo)
            registros = registros.Where(r => EF.Functions.ILike(r.Modulo, modulo, "\\"));
        if (Trecho(consulta.Entidade) is { } entidade)
            registros = registros.Where(r => EF.Functions.ILike(r.Entidade, entidade, "\\"));

        var total = await registros.CountAsync(ct);
        var pagina = await registros
            .OrderByDescending(r => r.OcorreuEm).ThenByDescending(r => r.Id)
            .Skip((consulta.Pagina - 1) * consulta.TamanhoDaPagina)
            .Take(consulta.TamanhoDaPagina)
            .ToListAsync(ct);

        var itens = pagina
            .Select(r => new RegistroDeAuditoriaDto(
                r.Id, r.OcorreuEm, r.UsuarioId, r.UsuarioNome, r.Modulo, r.Entidade, r.EntidadeId, r.Operacao,
                r.Campos.Select(c => new CampoAuditadoDto(c.Campo, c.Antes, c.Depois, c.Omitido)).ToList()))
            .ToList();

        return new PaginaDeAuditoriaDto(itens, consulta.Pagina, consulta.TamanhoDaPagina, total);
    }

    /// <summary>Padrão "contém" do ILIKE, com <c>%</c>, <c>_</c> e <c>\</c> do filtro tratados como texto.</summary>
    private static string? Trecho(string? filtro)
    {
        if (string.IsNullOrWhiteSpace(filtro))
            return null;

        var escapado = filtro.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        return $"%{escapado}%";
    }
}
