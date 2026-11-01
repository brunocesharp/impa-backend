using System.Net;
using Npgsql;

namespace Impa.Api.IntegrationTests;

public class HealthTests(ImpaApiFactory factory) : IClassFixture<ImpaApiFactory>
{
    [Fact]
    public async Task Health_responde_sem_autenticacao()
    {
        var resposta = await factory.CriarCliente().GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }

    [Fact]
    public async Task Migracao_do_host_cria_extensoes_unaccent_e_pg_trgm()
    {
        _ = factory.CriarCliente(); // sobe o Host, que aplica as migrações

        Assert.Equal(2L, await Escalar(
            "select count(*) from pg_extension where extname in ('unaccent', 'pg_trgm')"));
    }

    [Fact]
    public async Task Migracao_do_modulo_acesso_cria_tabela_de_usuarios_no_schema_acesso()
    {
        _ = factory.CriarCliente();

        Assert.Equal(1L, await Escalar(
            "select count(*) from information_schema.tables where table_schema = 'acesso' and table_name = 'usuarios'"));
    }

    private async Task<object?> Escalar(string sql)
    {
        await using var conexao = new NpgsqlConnection(factory.ConnectionString);
        await conexao.OpenAsync();
        await using var comando = new NpgsqlCommand(sql, conexao);
        return await comando.ExecuteScalarAsync();
    }
}
