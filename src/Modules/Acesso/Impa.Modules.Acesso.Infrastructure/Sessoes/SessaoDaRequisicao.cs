namespace Impa.Modules.Acesso.Infrastructure.Sessoes;

/// <summary>
/// Sessão (<c>acesso.sessoes</c>) que autenticou a requisição, guardada nas features do <c>HttpContext</c> pelo
/// <see cref="SessaoTicketStore"/>. Usada para ligar a liberação por administrador à sessão do operador (story 1-5).
/// </summary>
public sealed record SessaoDaRequisicao(Guid Id);
