using Ardalis.Result;
using Impa.Modules.Acesso.Application.Usuarios;
using Impa.Modules.Acesso.Application.Usuarios.Commands.DesativarUsuario;

namespace Impa.Modules.Acesso.Tests;

/// <summary>Story 1-2, T4 — desativar usuário (CA-2, CA-4).</summary>
public class DesativarUsuarioCommandHandlerTests
{
    private readonly GestaoFalsa _usuarios = new();

    private Task<Result> Desativar(Guid id) =>
        new DesativarUsuarioCommandHandler(_usuarios).Handle(new DesativarUsuarioCommand(id), CancellationToken.None);

    [Fact]
    public async Task Usuario_existente_e_desativado()
    {
        var id = Guid.NewGuid();
        _usuarios.Existentes.Add(id);

        var resultado = await Desativar(id);

        Assert.True(resultado.IsSuccess);
        Assert.Equal([id], _usuarios.Desativados);
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

    private sealed class GestaoFalsa : IGestaoUsuarios
    {
        public HashSet<Guid> Existentes { get; } = [];
        public List<Guid> Desativados { get; } = [];

        public Task<bool> ExisteAsync(Guid usuarioId, CancellationToken ct) => Task.FromResult(Existentes.Contains(usuarioId));

        public Task DesativarAsync(Guid usuarioId, CancellationToken ct)
        {
            Desativados.Add(usuarioId);
            return Task.CompletedTask;
        }
    }
}
