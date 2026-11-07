using System.Net;
using System.Net.Http.Json;
using Ardalis.Result;
using Impa.Modules.Acesso.Application.Perfis;
using Impa.Modules.Acesso.Application.Usuarios;
using Impa.Modules.Acesso.Application.Usuarios.Commands.DesativarUsuario;
using Impa.Modules.Acesso.Application.Usuarios.Commands.TrocarPerfilDoUsuario;
using Impa.Modules.Acesso.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Impa.Api.IntegrationTests;

/// <summary>Story 1-3, T4/T8 — cenário 1.6: a loja nunca fica sem administrador ativo (CA-2, CA-5).</summary>
public class UltimoAdministradorTests(ImpaApiFactory factory) : IClassFixture<ImpaApiFactory>
{
    private const string Mensagem = "É preciso manter pelo menos um administrador ativo";

    private sealed record ErroResposta(string Mensagem);

    private async Task<Result> EnviarAsync(IRequest<Result> comando)
    {
        await using var escopo = factory.Services.CreateAsyncScope();
        return await escopo.ServiceProvider.GetRequiredService<ISender>().Send(comando);
    }

    private async Task<Guid> PerfilIdAsync(string nome)
    {
        await using var escopo = factory.Services.CreateAsyncScope();
        return (await escopo.ServiceProvider.GetRequiredService<RoleManager<Perfil>>().FindByNameAsync(nome))!.Id;
    }

    [Fact]
    public async Task Cenario_1_6_a_unica_administradora_nao_consegue_se_desativar()
    {
        var carla = await factory.IdDoUsuarioAsync(ImpaApiFactory.AdminUsuario);
        var cliente = await ClienteApi.AbrirAsync(factory);
        await cliente.EntrarAsync(ImpaApiFactory.AdminUsuario, ImpaApiFactory.AdminSenha);

        var resposta = await cliente.PostAsync($"/api/usuarios/{carla}/desativar");

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal(Mensagem, (await resposta.Content.ReadFromJsonAsync<ErroResposta>())!.Mensagem);
        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/api/auth/me")).StatusCode); // sessão continua
    }

    [Fact]
    public async Task Cenario_1_6_a_unica_administradora_nao_pode_virar_Operador_de_caixa()
    {
        var carla = await factory.IdDoUsuarioAsync(ImpaApiFactory.AdminUsuario);
        var operador = await PerfilIdAsync(PerfisPadrao.OperadorDeCaixa.Nome);

        var resultado = await EnviarAsync(new TrocarPerfilDoUsuarioCommand(carla, operador));

        Assert.Equal(ResultStatus.Conflict, resultado.Status);
        Assert.Equal([Mensagem], resultado.Errors);
        await using var escopo = factory.Services.CreateAsyncScope();
        var usuarios = escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        Assert.Equal([PerfisPadrao.Administrador.Nome], await usuarios.GetRolesAsync((await usuarios.FindByIdAsync(carla.ToString()))!));
    }

    [Fact]
    public async Task Administrador_inativo_nao_conta_e_com_outro_ativo_a_troca_e_permitida()
    {
        var carla = await factory.IdDoUsuarioAsync(ImpaApiFactory.AdminUsuario);
        var operador = await PerfilIdAsync(PerfisPadrao.OperadorDeCaixa.Nome);
        var admin = await PerfilIdAsync(PerfisPadrao.Administrador.Nome);

        // Um segundo administrador, mas desativado: não conta.
        var inativo = await factory.CriarUsuarioAsync($"inativo-{Guid.NewGuid():N}"[..20], "senha-123456", "Inativo", PerfisPadrao.Administrador.Nome);
        await using (var escopo = factory.Services.CreateAsyncScope())
            await escopo.ServiceProvider.GetRequiredService<IGestaoUsuarios>().DesativarAsync(inativo, default);
        Assert.Equal(ResultStatus.Conflict, (await EnviarAsync(new DesativarUsuarioCommand(carla))).Status);

        // Bia ativa como administradora: agora Bia pode virar Operador de caixa e voltar (Carla segue admin).
        var bia = await factory.CriarUsuarioAsync($"bia-{Guid.NewGuid():N}"[..20], "senha-123456", "Bia", PerfisPadrao.Administrador.Nome);
        Assert.True((await EnviarAsync(new TrocarPerfilDoUsuarioCommand(bia, operador))).IsSuccess);
        Assert.Equal(ResultStatus.Conflict, (await EnviarAsync(new TrocarPerfilDoUsuarioCommand(carla, operador))).Status);
        Assert.True((await EnviarAsync(new TrocarPerfilDoUsuarioCommand(bia, admin))).IsSuccess);

        // Com Bia admin de novo, desativar Bia é permitido (sobra Carla).
        Assert.True((await EnviarAsync(new DesativarUsuarioCommand(bia))).IsSuccess);
    }
}

/// <summary>Banco próprio: o teste desativa administradores em paralelo.</summary>
public class UltimoAdministradorConcorrenciaTests(ImpaApiFactory factory) : IClassFixture<ImpaApiFactory>
{
    [Fact]
    public async Task Dois_administradores_se_desativando_ao_mesmo_tempo_sobra_um()
    {
        var carla = await factory.IdDoUsuarioAsync(ImpaApiFactory.AdminUsuario);
        var bia = await factory.CriarUsuarioAsync("bia", "senha-123456", "Bia", PerfisPadrao.Administrador.Nome);

        async Task<Result> Desativar(Guid id)
        {
            await using var escopo = factory.Services.CreateAsyncScope();
            return await escopo.ServiceProvider.GetRequiredService<ISender>().Send(new DesativarUsuarioCommand(id));
        }

        var resultados = await Task.WhenAll(Desativar(carla), Desativar(bia));

        Assert.Single(resultados, r => r.IsSuccess);
        Assert.Single(resultados, r => r.Status == ResultStatus.Conflict);
        await using var verificacao = factory.Services.CreateAsyncScope();
        Assert.Equal(1, await verificacao.ServiceProvider.GetRequiredService<IGestaoUsuarios>().ContarAdministradoresAtivosAsync(default));
    }
}
