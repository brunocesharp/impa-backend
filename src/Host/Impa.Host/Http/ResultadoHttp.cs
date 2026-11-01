using Ardalis.Result;
using IResultHttp = Microsoft.AspNetCore.Http.IResult;
using IResultado = Ardalis.Result.IResult;

namespace Impa.Host.Http;

/// <summary>Corpo de erro simples da API: <c>{ "mensagem": "..." }</c>.</summary>
public sealed record ErroDto(string Mensagem);

/// <summary>Converte o <c>Result</c> dos handlers na resposta HTTP. Um só lugar para o mapeamento de status.</summary>
public static class ResultadoHttp
{
    public static IResultHttp ParaHttp(this IResultado resultado) => resultado.Status switch
    {
        ResultStatus.Ok when resultado is Result => Results.NoContent(),
        ResultStatus.Ok => Results.Ok(resultado.GetValue()),
        ResultStatus.Created => Results.Created((string?)null, resultado.GetValue()),
        ResultStatus.NoContent => Results.NoContent(),
        ResultStatus.Invalid => Results.ValidationProblem(
            resultado.ValidationErrors
                .GroupBy(e => e.Identifier ?? string.Empty)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())),
        ResultStatus.Unauthorized => ComMensagem(StatusCodes.Status401Unauthorized, resultado),
        ResultStatus.Forbidden => ComMensagem(StatusCodes.Status403Forbidden, resultado),
        ResultStatus.NotFound => ComMensagem(StatusCodes.Status404NotFound, resultado),
        ResultStatus.Conflict => ComMensagem(StatusCodes.Status409Conflict, resultado),
        _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError,
            detail: string.Join("; ", resultado.Errors)),
    };

    private static IResultHttp ComMensagem(int status, IResultado resultado)
    {
        var mensagem = resultado.Errors.FirstOrDefault();
        return mensagem is null ? Results.StatusCode(status) : Results.Json(new ErroDto(mensagem), statusCode: status);
    }
}
