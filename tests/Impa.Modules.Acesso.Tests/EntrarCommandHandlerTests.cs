using Ardalis.Result;
using Impa.Modules.Acesso.Application.Autenticacao;
using Impa.Modules.Acesso.Application.Autenticacao.Commands.Entrar;

namespace Impa.Modules.Acesso.Tests;

/// <summary>R3 — a decisão do login fica no handler (ADR-013, regra 2) (CA-1, CA-2).</summary>
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

    private sealed class SessaoFalsa : ISessaoService
    {
        public Dictionary<string, (Guid Id, string Senha)> Usuarios { get; } = new(StringComparer.Ordinal);
        public List<Guid> SessoesAbertas { get; } = [];
        public List<string> ConferenciasFicticias { get; } = [];

        public Task<Guid?> BuscarUsuarioAsync(string login, CancellationToken ct) =>
            Task.FromResult(Usuarios.TryGetValue(login, out var u) ? u.Id : (Guid?)null);

        public Task<bool> SenhaConfereAsync(Guid usuarioId, string senha, CancellationToken ct) =>
            Task.FromResult(Usuarios.Values.Any(u => u.Id == usuarioId && u.Senha == senha));

        public Task ConferirSenhaFicticiaAsync(string senha, CancellationToken ct)
        {
            ConferenciasFicticias.Add(senha);
            return Task.CompletedTask;
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
