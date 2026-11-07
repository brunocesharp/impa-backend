using Ardalis.Result;
using Impa.Modules.Acesso.Application.Perfis;
using Impa.Modules.Acesso.Application.Perfis.Commands.AlterarPerfil;
using Impa.Modules.Acesso.Application.Perfis.Commands.CriarPerfil;
using Impa.Modules.Acesso.Application.Perfis.Commands.ExcluirPerfil;
using Impa.Modules.Acesso.Contracts;

namespace Impa.Modules.Acesso.Tests;

/// <summary>Story 1-3, T3 — criar, alterar e excluir perfis; Administrador protegido (CA-3, CA-5).</summary>
public class PerfisHandlersTests
{
    private readonly GestaoPerfisFalsa _perfis = new();

    private static readonly string[] DoBalcao = [Permissoes.PdvVender, Permissoes.CaixaOperar];

    [Fact]
    public async Task Criar_perfil_grava_nome_e_permissoes()
    {
        var resultado = await new CriarPerfilCommandHandler(_perfis)
            .Handle(new CriarPerfilCommand("Estoquista", [Permissoes.CatalogoVer, Permissoes.CatalogoEditar]), default);

        Assert.Equal(ResultStatus.Created, resultado.Status);
        Assert.Equal("Estoquista", resultado.Value.Nome);
        Assert.Equal([Permissoes.CatalogoEditar, Permissoes.CatalogoVer], resultado.Value.Permissoes);
        Assert.False(resultado.Value.Protegido);
    }

    [Fact]
    public async Task Criar_perfil_com_nome_em_uso_responde_conflito()
    {
        _perfis.Adicionar("Gerente", false, []);

        var resultado = await new CriarPerfilCommandHandler(_perfis).Handle(new CriarPerfilCommand(" gerente ", []), default);

        Assert.Equal(ResultStatus.Conflict, resultado.Status);
        Assert.Equal([MensagensPerfis.NomeEmUso], resultado.Errors);
    }

    [Fact]
    public async Task Alterar_permissoes_de_perfil_comum()
    {
        var id = _perfis.Adicionar("Operador de caixa", false, DoBalcao);

        var resultado = await new AlterarPerfilCommandHandler(_perfis)
            .Handle(new AlterarPerfilCommand(id, "Operador de caixa", [Permissoes.PdvVender]), default);

        Assert.True(resultado.IsSuccess);
        Assert.Equal([Permissoes.PdvVender], _perfis.Perfis[id].Permissoes);
    }

    [Fact]
    public async Task Administrador_nao_pode_ser_alterado()
    {
        var id = _perfis.Adicionar("Administrador", true, Permissoes.Todas);

        var resultado = await new AlterarPerfilCommandHandler(_perfis)
            .Handle(new AlterarPerfilCommand(id, "Administrador", [Permissoes.PdvVender]), default);

        Assert.Equal(ResultStatus.Conflict, resultado.Status);
        Assert.Equal([MensagensPerfis.AdministradorNaoAlteravel], resultado.Errors);
        Assert.Equal(Permissoes.Todas.Count, _perfis.Perfis[id].Permissoes.Count);
    }

    [Fact]
    public async Task Alterar_para_o_nome_de_outro_perfil_responde_conflito()
    {
        _perfis.Adicionar("Gerente", false, []);
        var id = _perfis.Adicionar("Estoquista", false, []);

        var resultado = await new AlterarPerfilCommandHandler(_perfis).Handle(new AlterarPerfilCommand(id, "Gerente", []), default);

        Assert.Equal(ResultStatus.Conflict, resultado.Status);
    }

    [Fact]
    public async Task Alterar_perfil_inexistente_responde_404()
    {
        var resultado = await new AlterarPerfilCommandHandler(_perfis).Handle(new AlterarPerfilCommand(Guid.NewGuid(), "X", []), default);

        Assert.Equal(ResultStatus.NotFound, resultado.Status);
    }

    [Fact]
    public async Task Excluir_perfil_sem_usuarios()
    {
        var id = _perfis.Adicionar("Temporário", false, []);

        var resultado = await new ExcluirPerfilCommandHandler(_perfis).Handle(new ExcluirPerfilCommand(id), default);

        Assert.True(resultado.IsSuccess);
        Assert.DoesNotContain(id, _perfis.Perfis.Keys);
    }

    [Fact]
    public async Task Excluir_perfil_com_usuarios_responde_conflito()
    {
        var id = _perfis.Adicionar("Operador de caixa", false, DoBalcao, usuarios: 2);

        var resultado = await new ExcluirPerfilCommandHandler(_perfis).Handle(new ExcluirPerfilCommand(id), default);

        Assert.Equal(ResultStatus.Conflict, resultado.Status);
        Assert.Equal([MensagensPerfis.PerfilComUsuarios], resultado.Errors);
        Assert.Contains(id, _perfis.Perfis.Keys);
    }

    [Fact]
    public async Task Administrador_nao_pode_ser_excluido()
    {
        var id = _perfis.Adicionar("Administrador", true, Permissoes.Todas);

        var resultado = await new ExcluirPerfilCommandHandler(_perfis).Handle(new ExcluirPerfilCommand(id), default);

        Assert.Equal(ResultStatus.Conflict, resultado.Status);
        Assert.Equal([MensagensPerfis.AdministradorNaoExcluivel], resultado.Errors);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("Estoquista", true)]
    public void Validator_de_criar_exige_nome(string nome, bool valido) =>
        Assert.Equal(valido, new CriarPerfilCommandValidator().Validate(new CriarPerfilCommand(nome, [])).IsValid);

    [Fact]
    public void Validator_recusa_permissao_fora_do_catalogo_e_nome_longo()
    {
        Assert.False(new CriarPerfilCommandValidator().Validate(new CriarPerfilCommand("X", ["nao.existe"])).IsValid);
        Assert.False(new AlterarPerfilCommandValidator().Validate(new AlterarPerfilCommand(Guid.NewGuid(), new string('a', 61), [])).IsValid);
    }
}

internal sealed class GestaoPerfisFalsa : IGestaoPerfis
{
    public Dictionary<Guid, PerfilDto> Perfis { get; } = [];

    public Guid Adicionar(string nome, bool protegido, IReadOnlyList<string> permissoes, int usuarios = 0)
    {
        var id = Guid.NewGuid();
        Perfis[id] = new PerfilDto(id, nome, protegido, permissoes.Order().ToList(), usuarios);
        return id;
    }

    public Task<IReadOnlyList<PerfilDto>> ListarAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PerfilDto>>(Perfis.Values.ToList());

    public Task<PerfilDto?> ObterAsync(Guid id, CancellationToken ct) => Task.FromResult(Perfis.GetValueOrDefault(id));

    public Task<bool> NomeEmUsoAsync(string nome, Guid? exceto, CancellationToken ct) =>
        Task.FromResult(Perfis.Values.Any(p => p.Id != exceto && string.Equals(p.Nome, nome, StringComparison.OrdinalIgnoreCase)));

    public Task<PerfilDto> CriarAsync(string nome, IReadOnlyCollection<string> permissoes, CancellationToken ct)
    {
        var id = Adicionar(nome, false, permissoes.ToList());
        return Task.FromResult(Perfis[id]);
    }

    public Task<PerfilDto> AlterarAsync(Guid id, string nome, IReadOnlyCollection<string> permissoes, CancellationToken ct)
    {
        Perfis[id] = Perfis[id] with { Nome = nome, Permissoes = permissoes.Order().ToList() };
        return Task.FromResult(Perfis[id]);
    }

    public Task ExcluirAsync(Guid id, CancellationToken ct)
    {
        Perfis.Remove(id);
        return Task.CompletedTask;
    }
}
