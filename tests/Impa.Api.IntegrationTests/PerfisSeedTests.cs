using System.Security.Claims;
using Impa.Modules.Acesso.Application.Perfis;
using Impa.Modules.Acesso.Contracts;
using Impa.Modules.Acesso.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Impa.Api.IntegrationTests;

/// <summary>Story 1-3, T2 — perfis padrão no banco (CA-4): seed idempotente que não desfaz o que o administrador alterou.</summary>
public class PerfisSeedTests(ImpaApiFactory factory) : IClassFixture<ImpaApiFactory>
{
    private AsyncServiceScope Escopo()
    {
        _ = factory.CriarCliente(); // sobe o Host: migrações e seed
        return factory.Services.CreateAsyncScope();
    }

    private static async Task<List<string>> PermissoesAsync(RoleManager<Perfil> perfis, Perfil perfil) =>
        (await perfis.GetClaimsAsync(perfil)).Where(c => c.Type == Permissoes.ClaimType).Select(c => c.Value).Order().ToList();

    [Fact]
    public async Task Administrador_e_protegido_e_Operador_de_caixa_nao()
    {
        await using var escopo = Escopo();
        var perfis = escopo.ServiceProvider.GetRequiredService<RoleManager<Perfil>>();

        Assert.True((await perfis.FindByNameAsync(PerfisPadrao.Administrador.Nome))!.Protegido);
        Assert.False((await perfis.FindByNameAsync(PerfisPadrao.OperadorDeCaixa.Nome))!.Protegido);
    }

    [Fact]
    public async Task Seed_nao_desfaz_a_alteracao_do_administrador_no_Operador_de_caixa()
    {
        await using var escopo = Escopo();
        var servicos = escopo.ServiceProvider;
        var perfis = servicos.GetRequiredService<RoleManager<Perfil>>();
        var operador = (await perfis.FindByNameAsync(PerfisPadrao.OperadorDeCaixa.Nome))!;

        // RN-1.5: o administrador pode alterar o Operador de caixa.
        Assert.True((await perfis.RemoveClaimAsync(operador, new Claim(Permissoes.ClaimType, Permissoes.ClientesEditar))).Succeeded);
        Assert.True((await perfis.AddClaimAsync(operador, new Claim(Permissoes.ClaimType, Permissoes.CaixaConsultar))).Succeeded);
        try
        {
            await AcessoSeed.ExecutarAsync(servicos, null);

            var depois = await PermissoesAsync(perfis, (await perfis.FindByNameAsync(PerfisPadrao.OperadorDeCaixa.Nome))!);
            Assert.DoesNotContain(Permissoes.ClientesEditar, depois);
            Assert.Contains(Permissoes.CaixaConsultar, depois);
        }
        finally
        {
            await perfis.RemoveClaimAsync(operador, new Claim(Permissoes.ClaimType, Permissoes.CaixaConsultar));
            await perfis.AddClaimAsync(operador, new Claim(Permissoes.ClaimType, Permissoes.ClientesEditar));
        }
    }

    [Fact]
    public async Task Seed_devolve_ao_Administrador_toda_permissao_do_catalogo_e_o_marca_como_protegido()
    {
        await using var escopo = Escopo();
        var servicos = escopo.ServiceProvider;
        var perfis = servicos.GetRequiredService<RoleManager<Perfil>>();
        var admin = (await perfis.FindByNameAsync(PerfisPadrao.Administrador.Nome))!;

        // Simula um banco de antes da 1.3: sem perfis.gerenciar.
        Assert.True((await perfis.RemoveClaimAsync(admin, new Claim(Permissoes.ClaimType, Permissoes.PerfisGerenciar))).Succeeded);

        await AcessoSeed.ExecutarAsync(servicos, null);

        Assert.Equal(Permissoes.Todas.Order(), await PermissoesAsync(perfis, (await perfis.FindByNameAsync(PerfisPadrao.Administrador.Nome))!));
    }

    [Fact]
    public async Task Na_instalacao_o_Operador_de_caixa_nasce_com_a_lista_da_RN_1_5_e_excluido_depois_nao_volta()
    {
        await using var escopo = Escopo();
        var servicos = escopo.ServiceProvider;
        var perfis = servicos.GetRequiredService<RoleManager<Perfil>>();
        var operador = (await perfis.FindByNameAsync(PerfisPadrao.OperadorDeCaixa.Nome))!;
        var permissoes = await PermissoesAsync(perfis, operador);

        Assert.Equal(PerfisPadrao.OperadorDeCaixa.Permissoes.Order(), permissoes);

        // Excluído pelo administrador (sem usuários): a próxima subida não o recria.
        Assert.True((await perfis.DeleteAsync(operador)).Succeeded);
        try
        {
            await AcessoSeed.ExecutarAsync(servicos, null);

            Assert.Null(await perfis.FindByNameAsync(PerfisPadrao.OperadorDeCaixa.Nome));
        }
        finally
        {
            var recriado = new Perfil(PerfisPadrao.OperadorDeCaixa.Nome);
            await perfis.CreateAsync(recriado);
            foreach (var p in permissoes)
                await perfis.AddClaimAsync(recriado, new Claim(Permissoes.ClaimType, p));
        }
    }
}
