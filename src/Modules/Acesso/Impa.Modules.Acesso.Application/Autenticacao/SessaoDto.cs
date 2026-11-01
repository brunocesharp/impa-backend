namespace Impa.Modules.Acesso.Application.Autenticacao;

/// <summary>Resposta de <c>GET /api/auth/me</c>: quem está na sessão e o que pode fazer.</summary>
public sealed record SessaoDto(string Usuario, string Nome, string Perfil, IReadOnlyList<string> Permissoes);
