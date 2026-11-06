namespace Impa.Api.IntegrationTests;

/// <summary>
/// R15/R20 — o hash fictício é calculado na inicialização, não na primeira tentativa com login inexistente (CA-2).
/// Classe própria, com uma <see cref="ImpaApiFactory"/> só dela: o <c>HashFicticio</c> é singleton do contêiner,
/// e qualquer login inexistente anterior na mesma factory já o teria calculado, escondendo a regressão.
/// Manter um único teste nesta classe.
/// </summary>
public class HashFicticioTests(ImpaApiFactory factory) : IClassFixture<ImpaApiFactory>
{
    [Fact]
    public async Task Primeiro_login_inexistente_depois_da_subida_nao_calcula_o_hash_ficticio()
    {
        _ = factory.Services; // sobe o Host: migrações, seed (que calcula hashes) e PrepararAcesso
        factory.Hasher.Zerar();

        var cliente = await ClienteApi.AbrirAsync(factory);
        await cliente.EntrarAsync("nao-existe", "errada123");

        // Só a conferência: nenhum PBKDF2 a mais calculando o hash fictício nesta requisição.
        Assert.Equal(0, factory.Hasher.Hashes);
        Assert.Equal(1, factory.Hasher.Conferencias);
    }
}
