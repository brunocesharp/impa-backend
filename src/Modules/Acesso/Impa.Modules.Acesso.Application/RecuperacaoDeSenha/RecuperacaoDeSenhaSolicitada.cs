using Impa.Shared.Messaging;

namespace Impa.Modules.Acesso.Application.RecuperacaoDeSenha;

/// <summary>
/// Um usuário ativo com e-mail pediu "esqueci minha senha" (RN-1.11). Interno do Acesso: o próprio módulo consome
/// (Inbox do Acesso), gera o link e manda o e-mail fora do fluxo da requisição. Só o id viaja: o token nunca é
/// gravado no Outbox (decisão de 14/11/2026).
/// </summary>
[EventoDeIntegracao("acesso.recuperacao-de-senha-solicitada", 1)]
public sealed record RecuperacaoDeSenhaSolicitada(Guid UsuarioId) : IntegrationEvent
{
    public override string? ChaveDeCorrelacao => $"usuario:{UsuarioId}";
}
