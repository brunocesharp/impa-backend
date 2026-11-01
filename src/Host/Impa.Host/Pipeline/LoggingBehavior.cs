using System.Diagnostics;
using MediatR;

namespace Impa.Host.Pipeline;

/// <summary>
/// Registra cada request (só o nome, nunca o conteúdo: o login carrega a senha), avisa quando passa de 500 ms
/// e registra exceções não tratadas antes de repassá-las.
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private const long LimiteLentoMs = 500;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var nome = typeof(TRequest).Name;
        var cronometro = Stopwatch.StartNew();

        try
        {
            var resposta = await next(ct);

            if (cronometro.ElapsedMilliseconds > LimiteLentoMs)
                logger.LogWarning("Request lento: {Request} levou {Ms} ms", nome, cronometro.ElapsedMilliseconds);
            else
                logger.LogDebug("{Request} concluído em {Ms} ms", nome, cronometro.ElapsedMilliseconds);

            return resposta;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exceção não tratada em {Request}", nome);
            throw;
        }
    }
}
