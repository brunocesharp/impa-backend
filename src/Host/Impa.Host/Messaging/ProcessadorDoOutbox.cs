using Impa.Shared.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Impa.Host.Messaging;

/// <summary>
/// Um ciclo do despachante: para cada Outbox registrado, entrega até um lote de pendentes, um evento por vez,
/// cada um na própria transação: lido com <c>FOR UPDATE SKIP LOCKED</c> (nenhum outro processo entrega o mesmo
/// evento ao mesmo tempo), entregue a cada handler, marcado e commitado antes do próximo. Assim o bloqueio e a
/// transação não abrangem o lote, e um handler lento não deixa os eventos anteriores sem confirmar (revisão 1, A2).
/// Se o processo cair no meio, a transação do evento é desfeita e ele volta a ser lido — o Inbox garante que o
/// efeito não se repete.
/// </summary>
public sealed class ProcessadorDoOutbox(
    IServiceScopeFactory escopos,
    RegistroDeMensageria registro,
    IOptions<OpcoesDaMensageria> opcoes,
    TimeProvider relogio,
    ILogger<ProcessadorDoOutbox> log)
{
    private OpcoesDaMensageria Opcoes => opcoes.Value;

    /// <summary>Processa um lote de cada Outbox. <c>true</c> se algum lote veio cheio (há mais a ler).</summary>
    public async Task<bool> ProcessarAsync(CancellationToken ct)
    {
        var mais = false;
        foreach (var tipoDoContexto in registro.Outboxes)
            mais |= await ProcessarLoteAsync(tipoDoContexto, ct) >= Opcoes.TamanhoDoLote;
        return mais;
    }

    /// <summary>Quando vence a próxima tentativa pendente (para o despachante não dormir além dela).</summary>
    public async Task<DateTimeOffset?> ProximaTentativaAsync(CancellationToken ct)
    {
        DateTimeOffset? proxima = null;
        foreach (var tipoDoContexto in registro.Outboxes)
        {
            await using var escopo = escopos.CreateAsyncScope();
            var contexto = (DbContext)escopo.ServiceProvider.GetRequiredService(tipoDoContexto);
            var desteOutbox = await contexto.Set<OutboxMensagem>()
                .Where(m => m.Situacao == SituacaoDoEvento.Pendente)
                .MinAsync(m => (DateTimeOffset?)m.ProximaTentativaEm, ct);
            if (desteOutbox is not null && (proxima is null || desteOutbox < proxima))
                proxima = desteOutbox;
        }

        return proxima;
    }

    private async Task<int> ProcessarLoteAsync(Type tipoDoContexto, CancellationToken ct)
    {
        await using var escopo = escopos.CreateAsyncScope();
        var contexto = (DbContext)escopo.ServiceProvider.GetRequiredService(tipoDoContexto);
        var outbox = contexto.Model.FindEntityType(typeof(OutboxMensagem))
            ?? throw new InvalidOperationException($"{tipoDoContexto.Name} não mapeia o Outbox (MapearOutbox).");

        // Schema e tabela vêm do modelo (não de entrada do usuário); o valor vai como parâmetro.
        var sql = $"SELECT * FROM \"{outbox.GetSchema()}\".\"{outbox.GetTableName()}\" " +
                  "WHERE situacao = 'Pendente' AND proxima_tentativa_em <= {0} " +
                  "ORDER BY ocorreu_em, id LIMIT 1 FOR UPDATE SKIP LOCKED";

        var processados = 0;
        while (processados < Opcoes.TamanhoDoLote)
        {
            await using var transacao = await contexto.Database.BeginTransactionAsync(ct);
            var mensagem = (await contexto.Set<OutboxMensagem>()
                .FromSqlRaw(sql, relogio.GetUtcNow())
                .ToListAsync(ct)).SingleOrDefault();
            if (mensagem is null)
                break;

            await EntregarAsync(mensagem, ct);
            await contexto.SaveChangesAsync(ct);
            await transacao.CommitAsync(ct);
            contexto.ChangeTracker.Clear();
            processados++;
        }

        return processados;
    }

    private async Task EntregarAsync(OutboxMensagem mensagem, CancellationToken ct)
    {
        var handlers = registro.HandlersPara(mensagem.Tipo, mensagem.Versao).ToList();
        var erros = new List<string>();

        if (handlers.Count > 0)
        {
            IIntegrationEvent? evento = null;
            try
            {
                evento = registro.Serializador.Desserializar(mensagem.Tipo, mensagem.Versao, mensagem.Conteudo);
            }
            catch (Exception e)
            {
                erros.Add($"leitura do evento: {e.GetType().Name}: {e.Message}");
            }

            if (evento is not null)
            {
                foreach (var handler in handlers)
                {
                    try
                    {
                        await ExecutarHandlerAsync(handler, mensagem.Id, evento, ct);
                    }
                    catch (Exception e) when (!ct.IsCancellationRequested)
                    {
                        erros.Add($"{handler.Nome}: {e.GetType().Name}: {e.Message}");
                        log.LogWarning(e,
                            "Handler {Handler} falhou no evento {EventoId} ({Tipo} v{Versao}), tentativa {Tentativa}",
                            handler.Nome, mensagem.Id, mensagem.Tipo, mensagem.Versao, mensagem.Tentativas + 1);
                    }
                }
            }
        }

        var agora = relogio.GetUtcNow();
        if (erros.Count == 0)
        {
            mensagem.MarcarEntregue(agora);
            return;
        }

        var erro = string.Join(Environment.NewLine, erros);
        var comFalha = mensagem.RegistrarFalha(
            erro, agora, Opcoes.IntervaloAposTentativa(mensagem.Tentativas + 1), Opcoes.LimiteDeTentativas);
        if (comFalha)
            log.LogError(
                "Evento {EventoId} ({Tipo} v{Versao}, chave {Chave}) ficou com falha após {Tentativas} tentativas: {Erro}",
                mensagem.Id, mensagem.Tipo, mensagem.Versao, mensagem.ChaveDeCorrelacao, mensagem.Tentativas, erro);
        else
            log.LogWarning(
                "Evento {EventoId} ({Tipo} v{Versao}) será tentado de novo em {ProximaTentativaEm} (tentativa {Tentativas})",
                mensagem.Id, mensagem.Tipo, mensagem.Versao, mensagem.ProximaTentativaEm, mensagem.Tentativas);
    }

    /// <summary>
    /// O handler roda num escopo próprio, numa transação do contexto do consumidor que grava o Inbox junto
    /// com o efeito. Evento + handler já no Inbox: ignorado (reentrega). Dois processos ao mesmo tempo:
    /// a chave do Inbox recusa o segundo, que é desfeito.
    /// </summary>
    private async Task ExecutarHandlerAsync(RegistroDeHandler registroDoHandler, Guid eventoId, IIntegrationEvent evento, CancellationToken ct)
    {
        await using var escopo = escopos.CreateAsyncScope();
        var servicos = escopo.ServiceProvider;
        var contexto = (DbContext)servicos.GetRequiredService(registroDoHandler.ContextoDoConsumidor);
        var inbox = contexto.Set<InboxMensagem>();

        await using var transacao = await contexto.Database.BeginTransactionAsync(ct);
        if (await inbox.AnyAsync(i => i.EventoId == eventoId && i.Handler == registroDoHandler.Nome, ct))
            return;

        await registroDoHandler.Executar(servicos.GetRequiredService(registroDoHandler.ClasseDoHandler), evento, ct);

        inbox.Add(InboxMensagem.Nova(eventoId, registroDoHandler.Nome, relogio.GetUtcNow()));
        await contexto.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
    }
}
