using Ardalis.Result;
using MediatR;

namespace Impa.Modules.Acesso.Application.Autenticacao.Commands.Sair;

/// <summary>Encerra a sessão atual (RN-1.3: a sessão termina no logout).</summary>
public sealed record SairCommand : IRequest<Result>;
