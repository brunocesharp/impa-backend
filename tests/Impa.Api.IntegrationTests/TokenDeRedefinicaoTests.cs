using Impa.Modules.Acesso.Infrastructure.RecuperacaoDeSenha;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Time.Testing;

namespace Impa.Api.IntegrationTests;

/// <summary>Story 1-8, T4 — token do link de redefinição (autêntico, com usuário, stamp e vencimento).</summary>
public class TokenDeRedefinicaoTests
{
    private readonly FakeTimeProvider _relogio = new(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));
    private readonly EphemeralDataProtectionProvider _chaves = new();

    private const string Email = "ANA@LOJAEXEMPLO.COM.BR";

    private TokenDeRedefinicao Criar() => new(_chaves, _relogio);

    [Fact]
    public void Le_o_usuario_e_o_stamp_de_um_token_gerado()
    {
        var id = Guid.NewGuid();
        var token = Criar().Gerar(id, "STAMP", Email, TimeSpan.FromMinutes(30));

        var lido = Criar().Ler(token);
        Assert.NotNull(lido);
        Assert.Equal(id, lido.UsuarioId);
        Assert.Equal("STAMP", lido.SecurityStamp);
        Assert.DoesNotContain(id.ToString("N"), token, StringComparison.OrdinalIgnoreCase); // cifrado, não só assinado
    }

    [Fact]
    public void Confere_o_email_para_o_qual_o_link_foi_enviado()
    {
        // R10 (revisão 1): o link só vale enquanto o e-mail do usuário for o de destino.
        var lido = Criar().Ler(Criar().Gerar(Guid.NewGuid(), "S", Email, TimeSpan.FromMinutes(30)))!;

        Assert.True(lido.FoiEnviadoPara(Email));
        Assert.False(lido.FoiEnviadoPara("OUTRO@LOJAEXEMPLO.COM.BR"));
        Assert.False(lido.FoiEnviadoPara(null));
        Assert.False(lido.FoiEnviadoPara(string.Empty));
    }

    [Fact]
    public void Vale_ate_30_minutos_e_nao_depois()
    {
        var token = Criar().Gerar(Guid.NewGuid(), "S", Email, TimeSpan.FromMinutes(30));

        _relogio.Advance(TimeSpan.FromMinutes(30));
        Assert.NotNull(Criar().Ler(token));

        _relogio.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(Criar().Ler(token));
    }

    [Fact]
    public void Token_alterado_e_recusado()
    {
        var token = Criar().Gerar(Guid.NewGuid(), "S", Email, TimeSpan.FromMinutes(30));
        var alterado = (token[0] == 'A' ? 'B' : 'A') + token[1..];

        Assert.Null(Criar().Ler(alterado));
    }

    [Fact]
    public void Token_de_outro_proposito_ou_chave_e_recusado()
    {
        var outro = new EphemeralDataProtectionProvider();
        var token = new TokenDeRedefinicao(outro, _relogio).Gerar(Guid.NewGuid(), "S", Email, TimeSpan.FromMinutes(30));

        Assert.Null(Criar().Ler(token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("%%%")]
    [InlineData("abc")]
    public void Lixo_e_recusado_sem_excecao(string token)
    {
        Assert.Null(Criar().Ler(token));
    }
}
