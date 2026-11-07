using Ardalis.Result;
using Impa.Modules.Acesso.Application.Autenticacao;
using Impa.Modules.Acesso.Application.Autenticacao.Commands.Entrar;

namespace Impa.Modules.Acesso.Tests;

/// <summary>R3 — a decisão do login fica no handler (ADR-013, regra 2) (story 1-1 CA-1, CA-2; story 1-2 CA-1 a CA-4).</summary>
public class EntrarCommandHandlerTests
{
    private static readonly Guid AnaId = Guid.NewGuid();

    private readonly SessaoFalsa _sessao = new() { Usuarios = { ["ana"] = (AnaId, "balcao2026") } };

    private Task<Result> Entrar(string usuario, string senha) =>
        new EntrarCommandHandler(_sessao).Handle(new EntrarCommand(usuario, senha), CancellationToken.None);

    [Fact]
    public async Task Credencial_correta_abre_a_sessao()
    {
        var resultado = await Entrar("ana", "balcao2026");

        Assert.True(resultado.IsSuccess);
        Assert.Equal([AnaId], _sessao.SessoesAbertas);
    }

    [Fact]
    public async Task Senha_errada_e_recusada_com_a_mensagem_padrao_sem_abrir_sessao()
    {
        var resultado = await Entrar("ana", "errada123");

        Assert.Equal(ResultStatus.Unauthorized, resultado.Status);
        Assert.Equal([EntrarCommand.MensagemCredencialInvalida], resultado.Errors);
        Assert.Empty(_sessao.SessoesAbertas);
    }

    [Fact]
    public async Task Usuario_inexistente_recebe_a_mesma_resposta_e_gasta_uma_conferencia_de_senha()
    {
        var resultado = await Entrar("nao-existe", "errada123");

        Assert.Equal(ResultStatus.Unauthorized, resultado.Status);
        Assert.Equal([EntrarCommand.MensagemCredencialInvalida], resultado.Errors);
        Assert.Equal(["errada123"], _sessao.ConferenciasFicticias);
        Assert.Empty(_sessao.SessoesAbertas);
    }

    [Fact]
    public async Task Senha_errada_nao_usa_a_conferencia_ficticia()
    {
        await Entrar("ana", "errada123");

        Assert.Empty(_sessao.ConferenciasFicticias);
    }

    [Theory]
    [InlineData("ana ")]
    [InlineData("  ana")]
    public async Task Espacos_em_volta_do_login_sao_ignorados(string login)
    {
        var resultado = await Entrar(login, "balcao2026");

        Assert.True(resultado.IsSuccess);
    }

    [Fact]
    public async Task Senha_e_conferida_como_digitada_sem_trim()
    {
        var resultado = await Entrar("ana", "balcao2026 ");

        Assert.Equal(ResultStatus.Unauthorized, resultado.Status);
    }

    // ---- Story 1-2: bloqueio por tentativas (CA-1, CA-3) ----

    [Fact]
    public async Task Senha_errada_registra_uma_falha()
    {
        await Entrar("ana", "errada123");

        Assert.Equal(1, _sessao.Falhas[AnaId]);
    }

    [Fact]
    public async Task Quatro_erros_nao_bloqueiam_e_o_quinto_bloqueia_com_a_mensagem_de_bloqueio()
    {
        for (var i = 0; i < 4; i++)
            Assert.Equal([EntrarCommand.MensagemCredencialInvalida], (await Entrar("ana", "errada123")).Errors);

        var quinta = await Entrar("ana", "errada123");

        Assert.Equal(ResultStatus.Unauthorized, quinta.Status);
        Assert.Equal([EntrarCommand.MensagemBloqueado], quinta.Errors);
        Assert.Contains(AnaId, _sessao.Bloqueados);
    }

    [Fact]
    public async Task Senha_correta_com_a_conta_bloqueada_e_recusada_e_ainda_calcula_o_hash()
    {
        _sessao.Bloqueados.Add(AnaId);

        var resultado = await Entrar("ana", "balcao2026");

        Assert.Equal(ResultStatus.Unauthorized, resultado.Status);
        Assert.Equal([EntrarCommand.MensagemBloqueado], resultado.Errors);
        Assert.Empty(_sessao.SessoesAbertas);
        Assert.Equal(1, _sessao.ConferenciasDeSenha); // o tempo não separa a conta bloqueada das outras
    }

    [Fact]
    public async Task Senha_errada_com_a_conta_bloqueada_nao_conta_nova_falha()
    {
        _sessao.Bloqueados.Add(AnaId);

        await Entrar("ana", "errada123");

        Assert.False(_sessao.Falhas.ContainsKey(AnaId));
    }

    [Fact]
    public async Task Login_correto_zera_a_contagem_de_falhas()
    {
        await Entrar("ana", "errada123");
        await Entrar("ana", "errada123");

        await Entrar("ana", "balcao2026");

        Assert.Equal(0, _sessao.Falhas[AnaId]);
    }

    [Fact]
    public async Task Senha_correta_quando_a_conta_bloqueou_em_paralelo_e_recusada_sem_abrir_sessao()
    {
        // R2: a situação lida no começo dizia "livre", mas outra requisição bloqueou antes de zerar a contagem.
        _sessao.BloquearAoZerar = true;

        var resultado = await Entrar("ana", "balcao2026");

        Assert.Equal([EntrarCommand.MensagemBloqueado], resultado.Errors);
        Assert.Empty(_sessao.SessoesAbertas);
    }

    // ---- Story 1-2: usuário desativado (CA-2, CA-4) ----

    [Fact]
    public async Task Usuario_desativado_com_a_senha_correta_e_recusado_com_a_mensagem_padrao()
    {
        _sessao.Inativos.Add(AnaId);

        var resultado = await Entrar("ana", "balcao2026");

        Assert.Equal(ResultStatus.Unauthorized, resultado.Status);
        Assert.Equal([EntrarCommand.MensagemCredencialInvalida], resultado.Errors);
        Assert.Empty(_sessao.SessoesAbertas);
        Assert.Equal(1, _sessao.ConferenciasDeSenha);
    }

    [Fact]
    public async Task Usuario_desativado_nao_conta_falhas()
    {
        _sessao.Inativos.Add(AnaId);

        await Entrar("ana", "errada123");

        Assert.False(_sessao.Falhas.ContainsKey(AnaId));
    }

    private sealed class SessaoFalsa : ISessaoService
    {
        private const int MaximoDeFalhas = 5;

        public Dictionary<string, (Guid Id, string Senha)> Usuarios { get; } = new(StringComparer.Ordinal);
        public List<Guid> SessoesAbertas { get; } = [];
        public List<string> ConferenciasFicticias { get; } = [];
        public Dictionary<Guid, int> Falhas { get; } = [];
        public HashSet<Guid> Bloqueados { get; } = [];
        public HashSet<Guid> Inativos { get; } = [];
        public int ConferenciasDeSenha { get; private set; }

        public Task<Guid?> BuscarUsuarioAsync(string login, CancellationToken ct) =>
            Task.FromResult(Usuarios.TryGetValue(login, out var u) ? u.Id : (Guid?)null);

        public Task<bool> SenhaConfereAsync(Guid usuarioId, string senha, CancellationToken ct)
        {
            ConferenciasDeSenha++;
            return Task.FromResult(Usuarios.Values.Any(u => u.Id == usuarioId && u.Senha == senha));
        }

        public Task ConferirSenhaFicticiaAsync(string senha, CancellationToken ct)
        {
            ConferenciasFicticias.Add(senha);
            return Task.CompletedTask;
        }

        public Task<SituacaoDaConta> ObterSituacaoAsync(Guid usuarioId, CancellationToken ct) =>
            Task.FromResult(new SituacaoDaConta(Ativo: !Inativos.Contains(usuarioId), Bloqueada: Bloqueados.Contains(usuarioId)));

        public Task<bool> RegistrarFalhaAsync(Guid usuarioId, CancellationToken ct)
        {
            Falhas[usuarioId] = Falhas.GetValueOrDefault(usuarioId) + 1;
            if (Falhas[usuarioId] < MaximoDeFalhas)
                return Task.FromResult(false);

            Falhas[usuarioId] = 0;
            Bloqueados.Add(usuarioId);
            return Task.FromResult(true);
        }

        /// <summary>Simula outra requisição bloqueando a conta entre a leitura da situação e o zerar.</summary>
        public bool BloquearAoZerar { get; set; }

        public Task<bool> ZerarFalhasAsync(Guid usuarioId, CancellationToken ct)
        {
            if (BloquearAoZerar)
                Bloqueados.Add(usuarioId);
            if (Bloqueados.Contains(usuarioId))
                return Task.FromResult(false);

            Falhas[usuarioId] = 0;
            return Task.FromResult(true);
        }

        public Task AbrirAsync(Guid usuarioId, CancellationToken ct)
        {
            SessoesAbertas.Add(usuarioId);
            return Task.CompletedTask;
        }

        public Task SairAsync(CancellationToken ct) => Task.CompletedTask;

        public Task<SessaoDto?> ObterAtualAsync(CancellationToken ct) => Task.FromResult<SessaoDto?>(null);
    }
}
