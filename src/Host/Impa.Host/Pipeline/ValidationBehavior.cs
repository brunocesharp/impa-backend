using System.Reflection;
using Ardalis.Result;
using FluentValidation;
using MediatR;

namespace Impa.Host.Pipeline;

/// <summary>
/// Roda os validators (FluentValidation) antes do handler. Falhas viram <c>Result.Invalid</c> (HTTP 400)
/// quando o handler devolve <c>Result</c>/<c>Result&lt;T&gt;</c>; para outros tipos, lança <see cref="ValidationException"/>.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (!validators.Any())
            return await next(ct);

        var contexto = new ValidationContext<TRequest>(request);
        var resultados = await Task.WhenAll(validators.Select(v => v.ValidateAsync(contexto, ct)));
        var falhas = resultados.SelectMany(r => r.Errors).Where(f => f is not null).ToList();

        if (falhas.Count == 0)
            return await next(ct);

        var erros = falhas.Select(f => new ValidationError(f.PropertyName, f.ErrorMessage)).ToList();

        // Result e Result<T> declaram o próprio Invalid(IEnumerable<ValidationError>) estático.
        var invalid = typeof(TResponse).GetMethod(
            nameof(Result.Invalid),
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
            [typeof(IEnumerable<ValidationError>)]);

        if (invalid is not null && typeof(Ardalis.Result.IResult).IsAssignableFrom(typeof(TResponse)))
            return (TResponse)invalid.Invoke(null, [erros])!;

        throw new ValidationException(falhas);
    }
}
