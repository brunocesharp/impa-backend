using Impa.Shared;

namespace Impa.Shared.Tests;

/// <summary>Story 3-7, T1 — CPF e CNPJ validados pelos dígitos verificadores (cenário 14.2, RN-14.1, RN-14.2).</summary>
public class DocumentoTests
{
    [Theory]
    [InlineData("529.982.247-25")]
    [InlineData("52998224725")]
    [InlineData(" 529 982 247 25 ")]
    public void CPF_valido_com_ou_sem_mascara(string texto)
    {
        Assert.True(Documento.TentarCriar(texto, out var documento, out var erro));
        Assert.Null(erro);
        Assert.Equal(TipoDeDocumento.Cpf, documento!.Tipo);
        Assert.Equal("52998224725", documento.Numero);
        Assert.Equal("529.982.247-25", documento.Formatado);
    }

    [Theory]
    [InlineData("11.222.333/0001-81")]
    [InlineData("11222333000181")]
    public void CNPJ_valido_com_ou_sem_mascara(string texto)
    {
        Assert.True(Documento.TentarCriar(texto, out var documento, out var erro));
        Assert.Null(erro);
        Assert.Equal(TipoDeDocumento.Cnpj, documento!.Tipo);
        Assert.Equal("11222333000181", documento.Numero);
        Assert.Equal("11.222.333/0001-81", documento.Formatado);
    }

    [Theory]
    [InlineData("111.111.111-11")] // cenário 14.2: sequência repetida passa na conta, mas é recusada
    [InlineData("000.000.000-00")]
    [InlineData("99999999999")]
    [InlineData("529.982.247-24")] // dígito verificador errado
    [InlineData("529.982.247-35")]
    public void CPF_invalido_e_recusado_com_a_mensagem_do_cenario(string texto)
    {
        Assert.False(Documento.TentarCriar(texto, out var documento, out var erro));
        Assert.Null(documento);
        Assert.Equal("CPF inválido", erro);
    }

    [Theory]
    [InlineData("11.222.333/0001-82")]
    [InlineData("11.222.333/0001-91")]
    [InlineData("11.111.111/1111-11")]
    [InlineData("00000000000000")]
    public void CNPJ_invalido_e_recusado(string texto)
    {
        Assert.False(Documento.TentarCriar(texto, out var documento, out var erro));
        Assert.Null(documento);
        Assert.Equal("CNPJ inválido", erro);
    }

    [Theory]
    [InlineData("5299822472")]      // 10 dígitos
    [InlineData("529982247251")]    // 12 dígitos
    [InlineData("529.982.247-2X")]  // letra
    [InlineData("abc")]
    public void Nem_CPF_nem_CNPJ_e_recusado(string texto)
    {
        Assert.False(Documento.TentarCriar(texto, out _, out var erro));
        Assert.Equal(Documento.MensagemInvalido, erro);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Vazio_nao_e_documento(string? texto)
    {
        Assert.True(Documento.EstaVazio(texto));
        Assert.False(Documento.TentarCriar(texto, out _, out _));
    }

    [Fact]
    public void Mesmo_numero_com_e_sem_mascara_e_o_mesmo_documento()
    {
        Assert.Equal(Documento.Criar("529.982.247-25"), Documento.Criar("52998224725"));
    }

    [Fact]
    public void Criar_com_documento_invalido_lanca()
    {
        Assert.Throws<ArgumentException>(() => Documento.Criar("111.111.111-11"));
    }

    [Theory]
    [InlineData("(31) 99999-0000", "31999990000")]
    [InlineData("529.982.247-25", "52998224725")]
    [InlineData(null, "")]
    public void Somente_digitos(string? texto, string esperado)
    {
        Assert.Equal(esperado, Documento.SomenteDigitos(texto));
    }
}
