using Ardalis.Result;
using FluentValidation;
using MediatR;

namespace Impa.Modules.Acesso.Application.Perfis.Commands.AlterarPerfil;

/// <summary>Troca nome e permissões de um perfil. O Administrador não pode ser alterado (RN-1.6).</summary>
public sealed record AlterarPerfilCommand(Guid Id, string Nome, IReadOnlyList<string> Permissoes) : IRequest<Result<PerfilDto>>;

public sealed class AlterarPerfilCommandValidator : AbstractValidator<AlterarPerfilCommand>
{
    public AlterarPerfilCommandValidator()
    {
        RuleFor(c => c.Id).NotEmpty().WithMessage("Informe o perfil");
        RegrasDoPerfil.Nome(RuleFor(c => c.Nome));
        RegrasDoPerfil.Permissoes(RuleFor(c => c.Permissoes));
    }
}

public sealed class AlterarPerfilCommandHandler(IGestaoPerfis perfis) : IRequestHandler<AlterarPerfilCommand, Result<PerfilDto>>
{
    public async Task<Result<PerfilDto>> Handle(AlterarPerfilCommand request, CancellationToken ct)
    {
        var atual = await perfis.ObterAsync(request.Id, ct);
        if (atual is null)
            return Result<PerfilDto>.NotFound(MensagensPerfis.NaoEncontrado);

        // RN-1.6: não pode perder permissões; renomear quebraria a referência dos perfis padrão. Fica imutável.
        if (atual.Protegido)
            return Result<PerfilDto>.Conflict(MensagensPerfis.AdministradorNaoAlteravel);

        var nome = RegrasDoPerfil.NormalizarNome(request.Nome);
        if (await perfis.NomeEmUsoAsync(nome, request.Id, ct))
            return Result<PerfilDto>.Conflict(MensagensPerfis.NomeEmUso);

        return Result.Success(await perfis.AlterarAsync(request.Id, nome, RegrasDoPerfil.NormalizarPermissoes(request.Permissoes), ct));
    }
}
