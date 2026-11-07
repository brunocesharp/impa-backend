namespace Impa.Modules.Acesso.Application.Autenticacao;

/// <summary>
/// Resposta de <c>GET /api/auth/me</c>: quem está na sessão e o que pode fazer. <paramref name="TrocaDeSenhaObrigatoria"/>:
/// o usuário está com a senha provisória e a SPA o leva para a troca de senha (RN-1.8, story 1.4).
/// </summary>
public sealed record SessaoDto(
    string Usuario, string Nome, string Perfil, IReadOnlyList<string> Permissoes, bool TrocaDeSenhaObrigatoria);
