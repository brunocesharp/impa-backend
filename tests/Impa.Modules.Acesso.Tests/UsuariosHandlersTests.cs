using Ardalis.Result;
using Impa.Modules.Acesso.Application.Autenticacao;
using Impa.Modules.Acesso.Application.Autenticacao.Commands.TrocarSenha;
using Impa.Modules.Acesso.Application.Perfis;
using Impa.Modules.Acesso.Application.Usuarios;
using Impa.Modules.Acesso.Application.Usuarios.Commands.CriarUsuario;
using Impa.Modules.Acesso.Application.Usuarios.Commands.RedefinirSenha;
using Impa.Modules.Acesso.Application.Usuarios.Queries.ListarPerfisDosUsuarios;

namespace Impa.Modules.Acesso.Tests;

/// <summary>Story 1-4, T1–T3 — criar usuário, redefinir senha e troca de senha (CA-1, CA-2, RN-1.8).</summary>
public class UsuariosHandlersTests
{
    private readonly CadastroFalso _cadastro = new();
    private readonly GestaoPerfisFalsa _perfis = new();

    [Fact]
    public void Senha_provisoria_gerada_e_temp_mais_4_digitos()
    {
        for (var i = 0; i < 50; i++)
            Assert.Matches("^temp-[0-9]{4}$", RegrasDoUsuario.GerarSenhaProvisoria());
    }

    [Fact]
    public async Task Criar_sem_senha_gera_a_provisoria_e_devolve_uma_vez()
    {
        var perfil = _perfis.Adicionar("Operador de caixa", false, []);

        var resultado = await new CriarUsuarioCommandHandler(_cadastro, _perfis)
            .Handle(new CriarUsuarioCommand(" ana ", " Ana ", " ", perfil, null), default);

        Assert.Equal(ResultStatus.Created, resultado.Status);
        Assert.Matches("^temp-[0-9]{4}$", resultado.Value.SenhaProvisoria);
        var (dados, senha) = Assert.Single(_cadastro.Criados);
        Assert.Equal(new NovoUsuario("ana", "Ana", null, perfil), dados);
        Assert.Equal(resultado.Value.SenhaProvisoria, senha);
    }

    [Fact]
    public async Task Criar_com_senha_informada_usa_a_informada()
    {
        var perfil = _perfis.Adicionar("Operador de caixa", false, []);

        var resultado = await new CriarUsuarioCommandHandler(_cadastro, _perfis)
            .Handle(new CriarUsuarioCommand("ana", "Ana", "ana@loja.com", perfil, "temp-4821"), default);

        Assert.Equal("temp-4821", resultado.Value.SenhaProvisoria);
    }

    [Fact]
    public async Task Criar_com_login_em_uso_responde_conflito_e_perfil_inexistente_404()
    {
        _cadastro.Logins.Add("ana");
        var handler = new CriarUsuarioCommandHandler(_cadastro, _perfis);

        Assert.Equal(ResultStatus.Conflict, (await handler.Handle(new CriarUsuarioCommand("ana", "Ana", null, Guid.NewGuid(), null), default)).Status);
        Assert.Equal(ResultStatus.NotFound, (await handler.Handle(new CriarUsuarioCommand("bia", "Bia", null, Guid.NewGuid(), null), default)).Status);
        Assert.Empty(_cadastro.Criados);
    }

    [Fact]
    public async Task R2_login_tomado_na_criacao_por_corrida_responde_conflito()
    {
        var perfil = _perfis.Adicionar("Operador de caixa", false, []);
        _cadastro.LoginTomadoNaCriacao = true; // livre na conferência, tomado no CreateAsync

        var resultado = await new CriarUsuarioCommandHandler(_cadastro, _perfis)
            .Handle(new CriarUsuarioCommand("ana", "Ana", null, perfil, null), default);

        Assert.Equal(ResultStatus.Conflict, resultado.Status);
        Assert.Equal([MensagensUsuarios.LoginEmUso], resultado.Errors);
    }

    [Fact]
    public async Task R3_perfis_da_tela_usuarios_trazem_so_id_nome_e_protegido()
    {
        var admin = _perfis.Adicionar("Administrador", true, ["usuarios.gerenciar"]);
        var caixa = _perfis.Adicionar("Operador de caixa", false, []);

        var resultado = await new ListarPerfisDosUsuariosQueryHandler(_perfis).Handle(new ListarPerfisDosUsuariosQuery(), default);

        Assert.True(resultado.IsSuccess);
        Assert.Equivalent(new[] { new PerfilResumoDto(admin, "Administrador", true), new PerfilResumoDto(caixa, "Operador de caixa", false) },
            resultado.Value);
    }

    [Fact]
    public void Validator_recusa_senha_curta_email_invalido_e_login_com_espaco()
    {
        var validador = new CriarUsuarioCommandValidator();
        var perfil = Guid.NewGuid();

        Assert.True(validador.Validate(new CriarUsuarioCommand("ana", "Ana", null, perfil, null)).IsValid);
        Assert.False(validador.Validate(new CriarUsuarioCommand("ana", "Ana", null, perfil, "curta")).IsValid);
        Assert.False(validador.Validate(new CriarUsuarioCommand("ana", "Ana", "sem-arroba", perfil, null)).IsValid);
        Assert.False(validador.Validate(new CriarUsuarioCommand("ana maria", "Ana", null, perfil, null)).IsValid);
    }

    [Fact]
    public async Task Redefinir_senha_marca_a_troca_obrigatoria_com_a_provisoria()
    {
        var gestao = new GestaoUsuariosExistentes { Existentes = { AnaId } };

        var resultado = await new RedefinirSenhaCommandHandler(gestao, _cadastro)
            .Handle(new RedefinirSenhaCommand(AnaId, "temp-4821"), default);

        Assert.True(resultado.IsSuccess);
        Assert.Equal("temp-4821", resultado.Value.SenhaProvisoria);
        Assert.Equal((AnaId, "temp-4821"), Assert.Single(_cadastro.Provisorias));
    }

    [Fact]
    public async Task Redefinir_senha_de_usuario_inexistente_responde_404()
    {
        var resultado = await new RedefinirSenhaCommandHandler(new GestaoUsuariosExistentes(), _cadastro)
            .Handle(new RedefinirSenhaCommand(AnaId, null), default);

        Assert.Equal(ResultStatus.NotFound, resultado.Status);
        Assert.Empty(_cadastro.Provisorias);
    }

    [Fact]
    public async Task Trocar_senha_confere_a_atual_antes_de_gravar()
    {
        var troca = new TrocaFalsa { Usuario = AnaId };
        var sessao = new SessaoComSenha(AnaId, "temp-4821");
        var handler = new TrocarSenhaCommandHandler(troca, sessao);

        var errada = await handler.Handle(new TrocarSenhaCommand("outra-senha", "nova-senha-2026"), default);
        Assert.Equal(ResultStatus.Invalid, errada.Status);
        Assert.Empty(troca.Trocas);

        var certa = await handler.Handle(new TrocarSenhaCommand("temp-4821", "nova-senha-2026"), default);
        Assert.True(certa.IsSuccess);
        Assert.Equal((AnaId, "nova-senha-2026"), Assert.Single(troca.Trocas));
    }

    [Fact]
    public async Task Trocar_senha_sem_sessao_responde_401()
    {
        var resultado = await new TrocarSenhaCommandHandler(new TrocaFalsa(), new SessaoComSenha(AnaId, "x"))
            .Handle(new TrocarSenhaCommand("x", "nova-senha-2026"), default);

        Assert.Equal(ResultStatus.Unauthorized, resultado.Status);
    }

    [Fact]
    public void Validator_da_troca_exige_8_caracteres_e_senha_diferente_da_atual()
    {
        var validador = new TrocarSenhaCommandValidator();

        Assert.True(validador.Validate(new TrocarSenhaCommand("temp-4821", "nova-senha")).IsValid);
        Assert.False(validador.Validate(new TrocarSenhaCommand("temp-4821", "curta")).IsValid);
        Assert.False(validador.Validate(new TrocarSenhaCommand("temp-4821", "temp-4821")).IsValid);
        Assert.False(validador.Validate(new TrocarSenhaCommand("", "nova-senha")).IsValid);
    }

    [Fact]
    public void Comandos_nao_mostram_senha_no_log()
    {
        Assert.DoesNotContain("temp-4821", new TrocarSenhaCommand("temp-4821", "nova-senha").ToString());
        Assert.DoesNotContain("nova-senha", new TrocarSenhaCommand("temp-4821", "nova-senha").ToString());
        Assert.DoesNotContain("temp-4821", new RedefinirSenhaCommand(AnaId, "temp-4821").ToString());
        Assert.DoesNotContain("temp-4821", new CriarUsuarioCommand("ana", "Ana", null, AnaId, "temp-4821").ToString());
        Assert.DoesNotContain("temp-4821", new SenhaProvisoriaDto(AnaId, "temp-4821").ToString());
    }

    private static readonly Guid AnaId = Guid.NewGuid();

    private sealed class CadastroFalso : ICadastroUsuarios
    {
        public HashSet<string> Logins { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<(NovoUsuario, string)> Criados { get; } = [];
        public List<(Guid, string)> Provisorias { get; } = [];

        public Task<IReadOnlyList<UsuarioDto>> ListarAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<UsuarioDto>>([]);

        public Task<UsuarioDto?> ObterAsync(Guid usuarioId, CancellationToken ct) => Task.FromResult<UsuarioDto?>(null);

        public Task<bool> LoginEmUsoAsync(string login, CancellationToken ct) => Task.FromResult(Logins.Contains(login));

        public bool LoginTomadoNaCriacao { get; set; }

        public Task<Guid?> CriarAsync(NovoUsuario dados, string senhaProvisoria, CancellationToken ct)
        {
            if (LoginTomadoNaCriacao)
                return Task.FromResult<Guid?>(null);
            Criados.Add((dados, senhaProvisoria));
            return Task.FromResult<Guid?>(Guid.NewGuid());
        }

        public Task AlterarDadosAsync(Guid usuarioId, string nome, string? email, CancellationToken ct) => Task.CompletedTask;

        public Task ReativarAsync(Guid usuarioId, CancellationToken ct) => Task.CompletedTask;

        public Task DefinirSenhaProvisoriaAsync(Guid usuarioId, string senhaProvisoria, CancellationToken ct)
        {
            Provisorias.Add((usuarioId, senhaProvisoria));
            return Task.CompletedTask;
        }
    }

    private sealed class GestaoUsuariosExistentes : IGestaoUsuarios
    {
        public HashSet<Guid> Existentes { get; } = [];

        public Task<bool> ExisteAsync(Guid usuarioId, CancellationToken ct) => Task.FromResult(Existentes.Contains(usuarioId));

        public Task<bool> EhAdministradorAtivoAsync(Guid usuarioId, CancellationToken ct) => Task.FromResult(false);

        public Task<int> ContarAdministradoresAtivosAsync(CancellationToken ct) => Task.FromResult(1);

        public Task<Result> ComAdministradoresTravadosAsync(Func<Task<Result>> operacao, CancellationToken ct) => operacao();

        public Task DesativarAsync(Guid usuarioId, CancellationToken ct) => Task.CompletedTask;

        public Task TrocarPerfilAsync(Guid usuarioId, Guid perfilId, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class TrocaFalsa : ITrocaDeSenha
    {
        public Guid? Usuario { get; init; }
        public List<(Guid, string)> Trocas { get; } = [];

        public Guid? UsuarioDaSessao() => Usuario;

        public Task<IReadOnlyList<string>> TrocarAsync(Guid usuarioId, string novaSenha, CancellationToken ct)
        {
            Trocas.Add((usuarioId, novaSenha));
            return Task.FromResult<IReadOnlyList<string>>([]);
        }
    }

    /// <summary>Só a conferência de senha importa para a troca; o resto do contrato não é usado.</summary>
    private sealed class SessaoComSenha(Guid usuarioId, string senha) : ISessaoService
    {
        public Task<bool> SenhaConfereAsync(Guid id, string informada, CancellationToken ct) =>
            Task.FromResult(id == usuarioId && informada == senha);

        public Task<Guid?> BuscarUsuarioAsync(string login, CancellationToken ct) => throw new NotSupportedException();
        public Task ConferirSenhaFicticiaAsync(string s, CancellationToken ct) => throw new NotSupportedException();
        public Task<SituacaoDaConta> ObterSituacaoAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> RegistrarFalhaAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ZerarFalhasAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task AbrirAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task SairAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<SessaoDto?> ObterAtualAsync(CancellationToken ct) => throw new NotSupportedException();
    }
}
