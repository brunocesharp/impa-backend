using Ardalis.Result;
using FluentValidation;
using MediatR;

namespace Impa.Modules.Acesso.Application.Perfis.Commands.CriarPerfil;

/// <summary>RN-1.4/RN-1.5: o administrador cria perfis — um conjunto de permissões do catálogo.</summary>
public sealed record CriarPerfilCommand(string Nome, IReadOnlyList<string> Permissoes) : IRequest<Result<PerfilDto>>;

public sealed class CriarPerfilCommandValidator : AbstractValidator<CriarPerfilCommand>
{
    public CriarPerfilCommandValidator()
    {
        RegrasDoPerfil.Nome(RuleFor(c => c.Nome));
        RegrasDoPerfil.Permissoes(RuleFor(c => c.Permissoes));
    }
}

public sealed class CriarPerfilCommandHandler(IGestaoPerfis perfis) : IRequestHandler<CriarPerfilCommand, Result<PerfilDto>>
{
    public async Task<Result<PerfilDto>> Handle(CriarPerfilCommand request, CancellationToken ct)
    {
        var nome = RegrasDoPerfil.NormalizarNome(request.Nome);
        if (await perfis.NomeEmUsoAsync(nome, null, ct))
            return Result<PerfilDto>.Conflict(MensagensPerfis.NomeEmUso);

        return Result.Created(await perfis.CriarAsync(nome, RegrasDoPerfil.NormalizarPermissoes(request.Permissoes), ct));
    }
}
