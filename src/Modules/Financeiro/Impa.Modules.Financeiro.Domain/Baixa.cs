using System.Globalization;

namespace Impa.Modules.Financeiro.Domain;

/// <summary>Quem estornou a baixa (usuário da sessão): id e nome de login.</summary>
public sealed record UsuarioDoEstorno(Guid Id, string Nome);

/// <summary>Situação da conta quanto ao pagamento (RN-23.5). A situação pelo vencimento (RN-23.6) é da story 11.3.</summary>
public enum SituacaoDoPagamento
{
    SemPagamento,
    ParcialmentePaga,
    Paga,
}

/// <summary>
/// Baixa (pagamento) de uma conta (RN-23.5): data do pagamento, valor pago e forma. Um estorno (RN-23.7) não apaga a
/// baixa: ela fica marcada como estornada, com quem, quando e o motivo, e deixa de contar no saldo.
/// </summary>
public sealed class Baixa
{
    public const int MotivoMaximo = 500;
    public const int UsuarioNomeMaximo = 256;

    private Baixa()
    {
    }

    public Guid Id { get; private set; }

    public Guid ContaId { get; private set; }

    public DateOnly Data { get; private set; }

    public decimal Valor { get; private set; }

    public Guid FormaDePagamentoId { get; private set; }

    public DateTimeOffset RegistradaEm { get; private set; }

    public DateTimeOffset? EstornadaEm { get; private set; }

    public Guid? EstornadaPorId { get; private set; }

    public string? EstornadaPorNome { get; private set; }

    public string? MotivoDoEstorno { get; private set; }

    public bool Estornada => EstornadaEm is not null;

    internal static Baixa Registrar(Guid contaId, DateOnly data, decimal valor, Guid formaDePagamentoId, DateTimeOffset agora) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            ContaId = contaId,
            Data = data,
            Valor = valor,
            FormaDePagamentoId = formaDePagamentoId,
            RegistradaEm = agora,
        };

    internal void Estornar(string motivo, UsuarioDoEstorno usuario, DateTimeOffset agora)
    {
        if (Estornada)
            throw new InvalidOperationException("Este pagamento já foi estornado");
        if (string.IsNullOrWhiteSpace(motivo))
            throw new ArgumentException("O motivo do estorno é obrigatório", nameof(motivo));

        EstornadaEm = agora;
        EstornadaPorId = usuario.Id;
        EstornadaPorNome = usuario.Nome;
        MotivoDoEstorno = motivo.Trim();
    }
}

/// <summary>Valores em reais no formato brasileiro ("R$ 1.234,56"), sem depender da cultura do servidor.</summary>
public static class Dinheiro
{
    public static string Formatar(decimal valor)
    {
        var invariante = valor.ToString("#,##0.00", CultureInfo.InvariantCulture);
        return "R$ " + invariante.Replace(',', '_').Replace('.', ',').Replace('_', '.');
    }
}
