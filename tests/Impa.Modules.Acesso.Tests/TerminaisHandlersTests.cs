using Ardalis.Result;
using Impa.Modules.Acesso.Application.Terminais;
using Impa.Modules.Acesso.Application.Terminais.Commands.RegistrarTerminal;
using Impa.Modules.Acesso.Application.Terminais.Commands.RenomearTerminal;
using Impa.Modules.Acesso.Application.Terminais.Commands.RevogarTerminal;
using Impa.Modules.Acesso.Application.Terminais.Queries.ListarTerminais;

namespace Impa.Modules.Acesso.Tests;

/// <summary>Story 1-6, T1/T2/T5 — token do terminal, registro com nome único, renomear e revogar (CA-1, CA-2).</summary>
public class TerminaisHandlersTests
{
    private static readonly Guid Admin = Guid.NewGuid();
    private readonly TerminaisFalsos _terminais = new();

    // ---- T1: token do terminal ----

    [Fact]
    public void Token_tem_256_bits_aleatorios_e_so_o_hash_e_guardado()
    {
        var token = TokenDoTerminal.Gerar();
        var outro = TokenDoTerminal.Gerar();

        Assert.NotEqual(token, outro);
        Assert.Equal(TokenDoTerminal.Bytes, Base64UrlDecode(token).Length);
        Assert.Equal(32, TokenDoTerminal.Bytes);

        var hash = TokenDoTerminal.Hash(token);
        Assert.Equal(32, hash.Length);
        Assert.Equal(hash, TokenDoTerminal.Hash(token));
        Assert.NotEqual(hash, TokenDoTerminal.Hash(outro));
        Assert.NotEqual(Base64UrlDecode(token), hash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("curto")]
    [InlineData("não é base64 url!!!!!!!!!!!!!!!!!!!!!!!!!!!")]
    public void Token_invalido_nao_tem_hash(string? token) => Assert.Null(TokenDoTerminal.HashSeValido(token));

    [Fact]
    public void Token_valido_tem_hash() =>
        Assert.NotNull(TokenDoTerminal.HashSeValido(TokenDoTerminal.Gerar()));

    // ---- T2: registrar ----

    [Fact]
    public async Task Registrar_grava_o_nome_quem_criou_e_so_o_hash_do_token()
    {
        var resultado = await new RegistrarTerminalCommandHandler(_terminais)
            .Handle(new RegistrarTerminalCommand(" Balcão 1 ", Admin, TokenAtual: null), default);

        Assert.Equal(ResultStatus.Created, resultado.Status);
        Assert.Equal("Balcão 1", resultado.Value.Terminal.Nome);
        var gravado = Assert.Single(_terminais.Itens.Values);
        Assert.Equal(Admin, gravado.CriadoPor);
        Assert.Equal(TokenDoTerminal.Hash(resultado.Value.Token), gravado.TokenHash);
    }

    [Fact]
    public async Task Registrar_com_nome_de_terminal_ativo_e_recusado()
    {
        _terminais.Adicionar("Balcão 1");

        var resultado = await new RegistrarTerminalCommandHandler(_terminais)
            .Handle(new RegistrarTerminalCommand("balcão 1", Admin, null), default);

        Assert.Equal(ResultStatus.Conflict, resultado.Status);
        Assert.Equal([MensagensTerminais.NomeEmUso], resultado.Errors);
    }

    [Fact]
    public async Task Registrar_com_nome_de_terminal_revogado_e_permitido()
    {
        _terminais.Adicionar("Balcão 1", revogado: true);

        var resultado = await new RegistrarTerminalCommandHandler(_terminais)
            .Handle(new RegistrarTerminalCommand("Balcão 1", Admin, null), default);

        Assert.Equal(ResultStatus.Created, resultado.Status);
    }

    [Fact]
    public async Task Dispositivo_que_ja_e_terminal_nao_e_registrado_de_novo()
    {
        var token = TokenDoTerminal.Gerar();
        _terminais.Adicionar("Balcão 1", token: token);

        var resultado = await new RegistrarTerminalCommandHandler(_terminais)
            .Handle(new RegistrarTerminalCommand("Balcão 2", Admin, token), default);

        Assert.Equal(ResultStatus.Conflict, resultado.Status);
        Assert.Equal([MensagensTerminais.DispositivoJaRegistrado("Balcão 1")], resultado.Errors);
    }

    [Fact]
    public async Task Dispositivo_com_terminal_revogado_pode_ser_registrado_de_novo()
    {
        var token = TokenDoTerminal.Gerar();
        _terminais.Adicionar("Balcão 1", token: token, revogado: true);

        var resultado = await new RegistrarTerminalCommandHandler(_terminais)
            .Handle(new RegistrarTerminalCommand("Balcão 1", Admin, token), default);

        Assert.Equal(ResultStatus.Created, resultado.Status);
        Assert.NotEqual(token, resultado.Value.Token);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Nome_vazio_e_invalido(string nome) =>
        Assert.False(new RegistrarTerminalCommandValidator().Validate(new RegistrarTerminalCommand(nome, Admin, null)).IsValid);

    [Fact]
    public void Nome_longo_e_invalido() =>
        Assert.False(new RegistrarTerminalCommandValidator()
            .Validate(new RegistrarTerminalCommand(new string('x', RegrasDoTerminal.NomeMaximo + 1), Admin, null)).IsValid);

    // ---- T2: renomear e revogar ----

    [Fact]
    public async Task Renomear_troca_o_nome()
    {
        var id = _terminais.Adicionar("Balcão 1");

        var resultado = await new RenomearTerminalCommandHandler(_terminais)
            .Handle(new RenomearTerminalCommand(id, "Caixa da frente"), default);

        Assert.True(resultado.IsSuccess);
        Assert.Equal("Caixa da frente", _terminais.Itens[id].Nome);
    }

    [Fact]
    public async Task Renomear_para_nome_de_outro_terminal_ativo_e_recusado()
    {
        var id = _terminais.Adicionar("Balcão 1");
        _terminais.Adicionar("Celular Loja");

        var resultado = await new RenomearTerminalCommandHandler(_terminais)
            .Handle(new RenomearTerminalCommand(id, "CELULAR LOJA"), default);

        Assert.Equal(ResultStatus.Conflict, resultado.Status);
    }

    [Fact]
    public async Task Renomear_mantendo_o_proprio_nome_e_permitido()
    {
        var id = _terminais.Adicionar("Balcão 1");

        var resultado = await new RenomearTerminalCommandHandler(_terminais)
            .Handle(new RenomearTerminalCommand(id, "balcão 1"), default);

        Assert.True(resultado.IsSuccess);
    }

    [Fact]
    public async Task Renomear_terminal_inexistente_ou_revogado()
    {
        var revogado = _terminais.Adicionar("Velho", revogado: true);

        Assert.Equal(ResultStatus.NotFound, (await new RenomearTerminalCommandHandler(_terminais)
            .Handle(new RenomearTerminalCommand(Guid.NewGuid(), "X"), default)).Status);
        Assert.Equal(ResultStatus.Conflict, (await new RenomearTerminalCommandHandler(_terminais)
            .Handle(new RenomearTerminalCommand(revogado, "X"), default)).Status);
    }

    [Fact]
    public async Task Revogar_marca_a_data_e_e_idempotente()
    {
        var id = _terminais.Adicionar("Balcão 1");

        Assert.True((await new RevogarTerminalCommandHandler(_terminais).Handle(new RevogarTerminalCommand(id), default)).IsSuccess);
        var primeira = _terminais.Itens[id].RevogadoEm;
        Assert.NotNull(primeira);
        Assert.True((await new RevogarTerminalCommandHandler(_terminais).Handle(new RevogarTerminalCommand(id), default)).IsSuccess);
        Assert.Equal(primeira, _terminais.Itens[id].RevogadoEm);

        Assert.Equal(ResultStatus.NotFound, (await new RevogarTerminalCommandHandler(_terminais)
            .Handle(new RevogarTerminalCommand(Guid.NewGuid()), default)).Status);
    }

    [Fact]
    public async Task Listar_indica_o_terminal_deste_dispositivo()
    {
        var token = TokenDoTerminal.Gerar();
        var atual = _terminais.Adicionar("Balcão 1", token: token);
        _terminais.Adicionar("Celular Loja");

        var lista = (await new ListarTerminaisQueryHandler(_terminais).Handle(new ListarTerminaisQuery(token), default)).Value;

        Assert.Equal(2, lista.Count);
        Assert.True(lista.Single(t => t.Id == atual).Atual);
        Assert.False(lista.Single(t => t.Id != atual).Atual);
    }

    private static byte[] Base64UrlDecode(string valor) => System.Buffers.Text.Base64Url.DecodeFromChars(valor);
}

/// <summary><see cref="ITerminais"/> em memória, com a mesma regra de nome (sem diferenciar maiúsculas, só ativos).</summary>
internal sealed class TerminaisFalsos : ITerminais
{
    public sealed class Item
    {
        public required Guid Id { get; init; }
        public required string Nome { get; set; }
        public required byte[] TokenHash { get; init; }
        public required Guid CriadoPor { get; init; }
        public DateTimeOffset CriadoEm { get; init; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? RevogadoEm { get; set; }
    }

    public Dictionary<Guid, Item> Itens { get; } = [];

    public Guid Adicionar(string nome, string? token = null, bool revogado = false)
    {
        var id = Guid.NewGuid();
        Itens[id] = new Item
        {
            Id = id,
            Nome = nome,
            TokenHash = TokenDoTerminal.Hash(token ?? TokenDoTerminal.Gerar()),
            CriadoPor = Guid.NewGuid(),
            RevogadoEm = revogado ? DateTimeOffset.UtcNow : null,
        };
        return id;
    }

    private TerminalDto Dto(Item i, byte[]? hashAtual) =>
        new(i.Id, i.Nome, "Administrador", i.CriadoEm, i.RevogadoEm, hashAtual is not null && i.TokenHash.SequenceEqual(hashAtual));

    public Task<IReadOnlyList<TerminalDto>> ListarAsync(byte[]? hashAtual, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<TerminalDto>>(Itens.Values.Select(i => Dto(i, hashAtual)).ToList());

    public Task<TerminalDto?> ObterAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(Itens.TryGetValue(id, out var i) ? Dto(i, null) : null);

    public Task<TerminalAtualDto?> ObterValidoAsync(byte[] tokenHash, CancellationToken ct) =>
        Task.FromResult(Itens.Values.Where(i => i.RevogadoEm is null && i.TokenHash.SequenceEqual(tokenHash))
            .Select(i => new TerminalAtualDto(i.Id, i.Nome)).SingleOrDefault());

    public Task<bool> NomeEmUsoAsync(string nome, Guid? exceto, CancellationToken ct) =>
        Task.FromResult(Itens.Values.Any(i => i.RevogadoEm is null && i.Id != exceto
            && string.Equals(i.Nome, nome, StringComparison.OrdinalIgnoreCase)));

    public Task<TerminalDto> RegistrarAsync(string nome, Guid criadoPor, byte[] tokenHash, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        Itens[id] = new Item { Id = id, Nome = nome, TokenHash = tokenHash, CriadoPor = criadoPor };
        return Task.FromResult(Dto(Itens[id], tokenHash));
    }

    public Task<TerminalDto> RenomearAsync(Guid id, string nome, CancellationToken ct)
    {
        Itens[id].Nome = nome;
        return Task.FromResult(Dto(Itens[id], null));
    }

    public Task RevogarAsync(Guid id, CancellationToken ct)
    {
        Itens[id].RevogadoEm ??= DateTimeOffset.UtcNow;
        return Task.CompletedTask;
    }
}
