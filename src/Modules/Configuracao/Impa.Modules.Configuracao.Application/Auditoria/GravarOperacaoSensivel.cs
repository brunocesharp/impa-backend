using Impa.Modules.Configuracao.Contracts.Auditoria;
using Impa.Modules.Configuracao.Domain.Auditoria;
using Impa.Shared.Messaging;

namespace Impa.Modules.Configuracao.Application.Auditoria;

/// <summary>
/// Grava a operação sensível publicada por qualquer módulo (CA-2). Roda no Inbox do Configuração: o despachante grava
/// o registro e o Inbox na mesma transação, então reentregar o evento não duplica o registro. Campos sensíveis ficam
/// sem os valores (CA-4).
/// </summary>
public sealed class GravarOperacaoSensivel(IRegistrosDeAuditoria registros, TimeProvider relogio)
    : IIntegrationEventHandler<OperacaoSensivelAuditada>
{
    public Task HandleAsync(OperacaoSensivelAuditada evento, CancellationToken ct)
    {
        var campos = (evento.Campos ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c.Campo))
            .Select(c => CampoAuditado.De(c.Campo, c.Antes, c.Depois, c.Sensivel));

        registros.Adicionar(RegistroDeAuditoria.Criar(
            evento.Id, evento.OcorreuEm, evento.UsuarioId, evento.UsuarioNome, evento.Modulo, evento.Entidade,
            evento.EntidadeId, evento.Operacao, campos, relogio.GetUtcNow()));
        return Task.CompletedTask;
    }
}
