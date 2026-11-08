using Ardalis.Result;
using FluentValidation;
using MediatR;

namespace Impa.Modules.Configuracao.Application.Auditoria.Queries.ConsultarAuditoria;

/// <summary>
/// Consulta das operações sensíveis, mais recentes primeiro, paginada (CA-1). Filtros opcionais:
/// período <paramref name="De"/> (inclusive) a <paramref name="Ate"/> (exclusive), e trechos do nome do usuário, do
/// módulo e da entidade (sem diferenciar maiúsculas).
/// </summary>
public sealed record ConsultarAuditoriaQuery(
    DateTimeOffset? De,
    DateTimeOffset? Ate,
    string? Usuario,
    string? Modulo,
    string? Entidade,
    int Pagina,
    int TamanhoDaPagina) : IRequest<Result<PaginaDeAuditoriaDto>>
{
    public const int TamanhoPadraoDaPagina = 20;
    public const int TamanhoMaximoDaPagina = 100;
    public const int FiltroMaximo = 100;
}

public sealed class ConsultarAuditoriaQueryValidator : AbstractValidator<ConsultarAuditoriaQuery>
{
    public ConsultarAuditoriaQueryValidator()
    {
        RuleFor(q => q.Pagina).GreaterThanOrEqualTo(1).WithMessage("A página começa em 1");
        RuleFor(q => q.TamanhoDaPagina)
            .InclusiveBetween(1, ConsultarAuditoriaQuery.TamanhoMaximoDaPagina)
            .WithMessage($"O tamanho da página vai de 1 a {ConsultarAuditoriaQuery.TamanhoMaximoDaPagina}");
        RuleFor(q => q.Ate)
            .GreaterThan(q => q.De).When(q => q.De is not null && q.Ate is not null)
            .WithMessage("O fim do período precisa ser depois do início");
        RuleFor(q => q.Usuario).MaximumLength(ConsultarAuditoriaQuery.FiltroMaximo);
        RuleFor(q => q.Modulo).MaximumLength(ConsultarAuditoriaQuery.FiltroMaximo);
        RuleFor(q => q.Entidade).MaximumLength(ConsultarAuditoriaQuery.FiltroMaximo);
    }
}

public sealed class ConsultarAuditoriaQueryHandler(IRegistrosDeAuditoria registros)
    : IRequestHandler<ConsultarAuditoriaQuery, Result<PaginaDeAuditoriaDto>>
{
    public async Task<Result<PaginaDeAuditoriaDto>> Handle(ConsultarAuditoriaQuery request, CancellationToken ct) =>
        Result.Success(await registros.ConsultarAsync(request, ct));
}
