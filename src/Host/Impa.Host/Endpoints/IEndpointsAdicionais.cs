namespace Impa.Host.Endpoints;

/// <summary>
/// Rotas registradas no DI e mapeadas pelo Host depois das próprias, com o mesmo pipeline (autenticação, CSRF,
/// permissões). Hoje usado pelos testes de integração para exercitar a autorização em funções que ainda não têm
/// endpoint (PDV, contas a pagar, fiscal, importação — story 1.3, cenário 1.5).
/// </summary>
public interface IEndpointsAdicionais
{
    void Mapear(IEndpointRouteBuilder app);
}
