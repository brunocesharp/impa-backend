using Ardalis.Result;
using Impa.Modules.Acesso.Application.Usuarios;
using Impa.Modules.Acesso.Application.Usuarios.Commands.DesativarUsuario;
using Impa.Modules.Acesso.Application.Usuarios.Commands.TrocarPerfilDoUsuario;
using Impa.Modules.Acesso.Contracts;

namespace Impa.Modules.Acesso.Tests;

/// <summary>Story 1-2, T4 — desativar usuário (CA-2, CA-4); story 1-3, T4 — último administrador (CA-2, CA-5).</summary>
public class DesativarUsuarioCommandHandlerTests
{
    private readonly GestaoFalsa _usuarios = new();
    private readonly GestaoPerfisFalsa _perfis = new();

    private Task<Result> Desativar(Guid id) =>
        new DesativarUsuarioCommandHandler(_usuarios).Handle(new DesativarUsuarioCommand(id), CancellationToken.None);

    private Task<Result> Trocar(Guid id, Guid perfil) =>
        new TrocarPerfilDoUsuarioCommandHandler(_usuarios, _perfis)
            .Handle(new TrocarPerfilDoUsuarioCommand(id, perfil), CancellationToken.None);

    [Fact]
    public async Task Usuario_existente_e_desativado()
    {
        var id = Guid.NewGuid();
        _usuarios.Existentes.Add(id);

        var resultado = await Desativar(id);

        Assert.True(resultado.IsSuccess);
        Assert.Equal([id], _usuarios.Desativados);
        Assert.Equal(1, _usuarios.Travas);
    }

    [Fact]
    public async Task Usuario_inexistente_responde_nao_encontrado_sem_desativar()
    {
        var resultado = await Desativar(Guid.NewGuid());

        Assert.Equal(ResultStatus.NotFound, resultado.Status);
        Assert.Equal([DesativarUsuarioCommand.MensagemNaoEncontrado], resultado.Errors);
        Assert.Empty(_usuarios.Desativados);
    }

    [Fact]
    public void Validator_recusa_id_vazio()
    {
        var resultado = new DesativarUsuarioCommandValidator().Validate(new DesativarUsuarioCommand(Guid.Empty));

        Assert.False(resultado.IsValid);
    }

    // ---- Cenário 1.6 ----

    [Fact]
    public async Task Cenario_1_6_unica_administradora_ativa_nao_pode_se_desativar()
    {
        var carla = _usuarios.NovoAdministrador();

        var resultado = await Desativar(carla);

        Assert.Equal(ResultStatus.Conflict, resultado.Status);
        Assert.Equal(["É preciso manter pelo menos um administrador ativo"], resultado.Errors);
        Assert.Empty(_usuarios.Desativados);
    }

    [Fact]
    public async Task Com_dois_administradores_ativos_um_pode_ser_desativado()
    {
        var carla = _usuarios.NovoAdministrador();
        _usuarios.NovoAdministrador();

        Assert.True((await Desativar(carla)).IsSuccess);
    }

    [Fact]
    public async Task Cenario_1_6_unica_administradora_ativa_nao_pode_virar_Operador_de_caixa()
    {
        var carla = _usuarios.NovoAdministrador();
        var operador = _perfis.Adicionar("Operador de caixa", false, [Permissoes.PdvVender]);

        var resultado = await Trocar(carla, operador);

        Assert.Equal(ResultStatus.Conflict, resultado.Status);
        Assert.Equal([MensagensUsuarios.UltimoAdministrador], resultado.Errors);
        Assert.Empty(_usuarios.Trocas);
    }

    [Fact]
    public async Task Trocar_perfil_de_operador_para_administrador_e_permitido()
    {
        var ana = Guid.NewGuid();
        _usuarios.Existentes.Add(ana);
        _usuarios.NovoAdministrador();
        var admin = _perfis.Adicionar("Administrador", true, Permissoes.Todas);

        Assert.True((await Trocar(ana, admin)).IsSuccess);
        Assert.Equal([(ana, admin)], _usuarios.Trocas);
    }

    [Fact]
    public async Task Trocar_para_perfil_inexistente_responde_404()
    {
        var ana = Guid.NewGuid();
        _usuarios.Existentes.Add(ana);

        var resultado = await Trocar(ana, Guid.NewGuid());

        Assert.Equal(ResultStatus.NotFound, resultado.Status);
        Assert.Equal([MensagensPerfisNaoEncontrado], resultado.Errors);
    }

    private const string MensagensPerfisNaoEncontrado = Application.Perfis.MensagensPerfis.NaoEncontrado;

    private sealed class GestaoFalsa : IGestaoUsuarios
    {
        public HashSet<Guid> Existentes { get; } = [];
        public HashSet<Guid> AdministradoresAtivos { get; } = [];
        public List<Guid> Desativados { get; } = [];
        public List<(Guid, Guid)> Trocas { get; } = [];
        public int Travas { get; private set; }

        public Guid NovoAdministrador()
        {
            var id = Guid.NewGuid();
            Existentes.Add(id);
            AdministradoresAtivos.Add(id);
            return id;
        }

        public Task<bool> ExisteAsync(Guid usuarioId, CancellationToken ct) => Task.FromResult(Existentes.Contains(usuarioId));

        public Task<bool> EhAdministradorAtivoAsync(Guid usuarioId, CancellationToken ct) =>
            Task.FromResult(AdministradoresAtivos.Contains(usuarioId));

        public Task<int> ContarAdministradoresAtivosAsync(CancellationToken ct) => Task.FromResult(AdministradoresAtivos.Count);

        public async Task<Result> ComAdministradoresTravadosAsync(Func<Task<Result>> operacao, CancellationToken ct)
        {
            Travas++;
            return await operacao();
        }

        public Task DesativarAsync(Guid usuarioId, CancellationToken ct)
        {
            Desativados.Add(usuarioId);
            AdministradoresAtivos.Remove(usuarioId);
            return Task.CompletedTask;
        }

        public Task TrocarPerfilAsync(Guid usuarioId, Guid perfilId, CancellationToken ct)
        {
            Trocas.Add((usuarioId, perfilId));
            return Task.CompletedTask;
        }
    }
}
