using Impa.Modules.Acesso.Application.Perfis;
using Impa.Modules.Acesso.Contracts;
using Impa.Modules.Acesso.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Impa.Api.IntegrationTests;

/// <summary>T2 — Identidade no módulo Acesso (CA-1, CA-3).</summary>
public class IdentidadeTests(ImpaApiFactory factory) : IClassFixture<ImpaApiFactory>
{
    private AsyncServiceScope Escopo()
    {
        _ = factory.CriarCliente(); // sobe o Host: migrações e seed
        return factory.Services.CreateAsyncScope();
    }

    [Fact]
    public async Task Seed_cria_os_perfis_padrao_com_as_permissoes_como_claims()
    {
        await using var escopo = Escopo();
        var perfis = escopo.ServiceProvider.GetRequiredService<RoleManager<Perfil>>();

        foreach (var padrao in PerfisPadrao.Todos)
        {
            var perfil = await perfis.FindByNameAsync(padrao.Nome);
            Assert.NotNull(perfil);

            var claims = await perfis.GetClaimsAsync(perfil);
            Assert.Equal(
                padrao.Permissoes.OrderBy(p => p),
                claims.Where(c => c.Type == Permissoes.ClaimType).Select(c => c.Value).OrderBy(p => p));
        }
    }

    [Fact]
    public async Task Seed_cria_o_primeiro_administrador_a_partir_das_variaveis_de_ambiente()
    {
        await using var escopo = Escopo();
        var usuarios = escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>();

        var admin = await usuarios.FindByNameAsync(ImpaApiFactory.AdminUsuario);

        Assert.NotNull(admin);
        Assert.Equal(ImpaApiFactory.AdminNome, admin.NomeExibicao);
        Assert.Equal([PerfisPadrao.Administrador.Nome], await usuarios.GetRolesAsync(admin));
        Assert.True(await usuarios.CheckPasswordAsync(admin, ImpaApiFactory.AdminSenha));
    }

    [Fact]
    public async Task Seed_rodado_de_novo_nao_duplica_perfis_permissoes_nem_administrador()
    {
        await using var escopo = Escopo();
        var servicos = escopo.ServiceProvider;

        await AcessoSeed.ExecutarAsync(servicos, new AdministradorInicial("outro-admin", "outra-senha-123", "Outro"));

        var usuarios = servicos.GetRequiredService<UserManager<Usuario>>();
        var perfis = servicos.GetRequiredService<RoleManager<Perfil>>();

        Assert.Single(await usuarios.GetUsersInRoleAsync(PerfisPadrao.Administrador.Nome));
        Assert.Null(await usuarios.FindByNameAsync("outro-admin"));
        Assert.Equal(PerfisPadrao.Todos.Count, perfis.Roles.Count());

        var administrador = await perfis.FindByNameAsync(PerfisPadrao.Administrador.Nome);
        Assert.Equal(Permissoes.Todas.Count, (await perfis.GetClaimsAsync(administrador!)).Count);
    }

    [Fact]
    public async Task Seed_com_login_do_administrador_ja_existente_sem_perfil_so_devolve_o_perfil()
    {
        await using var escopo = Escopo();
        var servicos = escopo.ServiceProvider;
        var usuarios = servicos.GetRequiredService<UserManager<Usuario>>();
        var admin = (await usuarios.FindByNameAsync(ImpaApiFactory.AdminUsuario))!;

        // Simula a falha entre criar o usuário e dar o perfil (ou o perfil retirado depois).
        Assert.True((await usuarios.RemoveFromRoleAsync(admin, PerfisPadrao.Administrador.Nome)).Succeeded);
        try
        {
            await AcessoSeed.ExecutarAsync(servicos, new AdministradorInicial(
                ImpaApiFactory.AdminUsuario, "outra-senha-do-env", "Outro nome"));

            var depois = (await usuarios.FindByNameAsync(ImpaApiFactory.AdminUsuario))!;
            Assert.Equal([PerfisPadrao.Administrador.Nome], await usuarios.GetRolesAsync(depois));
            // Usuário existente não é recriado nem tem senha ou nome trocados pelo seed.
            Assert.Equal(admin.Id, depois.Id);
            Assert.True(await usuarios.CheckPasswordAsync(depois, ImpaApiFactory.AdminSenha));
            Assert.Equal(ImpaApiFactory.AdminNome, depois.NomeExibicao);
        }
        finally
        {
            var atual = (await usuarios.FindByNameAsync(ImpaApiFactory.AdminUsuario))!;
            if (!await usuarios.IsInRoleAsync(atual, PerfisPadrao.Administrador.Nome))
                await usuarios.AddToRoleAsync(atual, PerfisPadrao.Administrador.Nome);
        }
    }

    [Fact]
    public async Task Senha_com_7_caracteres_e_recusada_na_criacao()
    {
        await using var escopo = Escopo();
        var usuarios = escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>();

        var resultado = await usuarios.CreateAsync(
            new Usuario { UserName = "curta", NomeExibicao = "Senha curta" }, "abcdefg");

        Assert.False(resultado.Succeeded);
        Assert.Contains(resultado.Errors, e => e.Code == nameof(IdentityErrorDescriber.PasswordTooShort));
    }

    [Fact]
    public async Task Senha_com_8_letras_minusculas_e_aceita_sem_exigir_digito_simbolo_ou_maiuscula()
    {
        await using var escopo = Escopo();
        var usuarios = escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>();

        var resultado = await usuarios.CreateAsync(
            new Usuario { UserName = "simples", NomeExibicao = "Senha simples" }, "abcdefgh");

        Assert.True(resultado.Succeeded, string.Join("; ", resultado.Errors.Select(e => e.Code)));
    }
}
