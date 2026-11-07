using System.Security.Claims;
using Impa.Modules.Acesso.Application.Perfis;
using Impa.Modules.Acesso.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Impa.Modules.Acesso.Infrastructure.Identity;

/// <summary>Dados do primeiro administrador (variáveis IMPA_ADMIN_USUARIO, IMPA_ADMIN_SENHA, IMPA_ADMIN_NOME).</summary>
public sealed record AdministradorInicial(string Usuario, string Senha, string Nome);

/// <summary>
/// Seed da inicialização, idempotente: garante os perfis padrão (RN-1.5, RN-1.6)
/// e cria o primeiro administrador se ainda não houver nenhum.
/// </summary>
public static class AcessoSeed
{
    public static async Task ExecutarAsync(
        IServiceProvider servicos, AdministradorInicial? administrador, CancellationToken ct = default)
    {
        var perfis = servicos.GetRequiredService<RoleManager<Perfil>>();
        var usuarios = servicos.GetRequiredService<UserManager<Usuario>>();
        var log = servicos.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AcessoSeed));

        var instalacao = !perfis.Roles.Any();
        foreach (var padrao in PerfisPadrao.Todos.Where(p => p.Protegido || instalacao))
            await GarantirPerfilAsync(perfis, padrao);

        if ((await usuarios.GetUsersInRoleAsync(PerfisPadrao.Administrador.Nome)).Count > 0)
            return;

        if (administrador is null)
        {
            log.LogWarning(
                "Nenhum administrador cadastrado e IMPA_ADMIN_USUARIO/IMPA_ADMIN_SENHA/IMPA_ADMIN_NOME não configurados: ninguém consegue entrar.");
            return;
        }

        // O login já pode existir sem o perfil (falha entre as duas gravações abaixo): só devolve o perfil,
        // sem recriar nem trocar senha/nome — senão toda subida lançaria DuplicateUserName. Mas só se for
        // mesmo a conta do .env (mesma senha) e sem outro perfil: um login reaproveitado por outra pessoa
        // não vira Administrador numa reinicialização.
        var usuario = await usuarios.FindByNameAsync(administrador.Usuario);
        if (usuario is null)
        {
            usuario = new Usuario { UserName = administrador.Usuario, NomeExibicao = administrador.Nome };
            Verificar(await usuarios.CreateAsync(usuario, administrador.Senha), "criar o primeiro administrador");
            log.LogInformation("Primeiro administrador {Usuario} criado.", administrador.Usuario);
        }
        else if (!await usuarios.CheckPasswordAsync(usuario, administrador.Senha)
                 || (await usuarios.GetRolesAsync(usuario)).Count > 0)
        {
            log.LogError(
                "Nenhum usuário com o perfil Administrador, e {Usuario} (IMPA_ADMIN_USUARIO) já existe com outra senha "
                + "ou outro perfil: o perfil Administrador não foi dado. Corrija IMPA_ADMIN_USUARIO/IMPA_ADMIN_SENHA.",
                administrador.Usuario);
            return;
        }
        else
        {
            log.LogWarning(
                "Nenhum usuário com o perfil Administrador; {Usuario} (IMPA_ADMIN_USUARIO) já existe sem perfil e recebe o perfil.",
                administrador.Usuario);
        }

        Verificar(await usuarios.AddToRoleAsync(usuario, PerfisPadrao.Administrador.Nome), "dar o perfil Administrador");
    }

    /// <summary>
    /// Story 1.3: as permissões dos perfis vivem no banco. Na instalação (nenhum perfil) os perfis padrão são criados
    /// com a lista da RN-1.5; depois disso, os não protegidos são do administrador (alterar, renomear, excluir) e o
    /// seed não os toca nem os recria. O Administrador
    /// (protegido, RN-1.6) fica sempre com exatamente o catálogo — permissão nova chega a ele na subida.
    /// </summary>
    private static async Task GarantirPerfilAsync(RoleManager<Perfil> perfis, PerfilPadrao padrao)
    {
        var perfil = await perfis.FindByNameAsync(padrao.Nome);
        var novo = perfil is null;
        if (perfil is null)
        {
            perfil = new Perfil(padrao.Nome, padrao.Protegido);
            Verificar(await perfis.CreateAsync(perfil), $"criar o perfil {padrao.Nome}");
        }
        else if (padrao.Protegido && !perfil.Protegido)
        {
            perfil.Proteger();
            Verificar(await perfis.UpdateAsync(perfil), $"proteger o perfil {padrao.Nome}");
        }

        if (!novo && !padrao.Protegido)
            return;

        var atuais = (await perfis.GetClaimsAsync(perfil)).Where(c => c.Type == Permissoes.ClaimType).ToList();

        foreach (var sobrando in atuais.Where(c => !padrao.Permissoes.Contains(c.Value)))
            Verificar(await perfis.RemoveClaimAsync(perfil, sobrando), $"remover {sobrando.Value} de {padrao.Nome}");

        foreach (var faltando in padrao.Permissoes.Where(p => atuais.All(c => c.Value != p)))
            Verificar(await perfis.AddClaimAsync(perfil, new Claim(Permissoes.ClaimType, faltando)),
                $"dar {faltando} a {padrao.Nome}");
    }

    private static void Verificar(IdentityResult resultado, string acao)
    {
        if (!resultado.Succeeded)
            throw new InvalidOperationException(
                $"Seed do Acesso: não foi possível {acao}: {string.Join("; ", resultado.Errors.Select(e => e.Description))}");
    }
}
