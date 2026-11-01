using Ardalis.Result;
using MediatR;

namespace Impa.Modules.Acesso.Application.Autenticacao.Queries.ObterSessaoAtual;

/// <summary>Quem está na sessão, com o perfil e as permissões (a SPA filtra o menu por elas).</summary>
public sealed record ObterSessaoAtualQuery : IRequest<Result<SessaoDto>>;
