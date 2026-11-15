namespace Impa.Modules.Acesso.Infrastructure.RecuperacaoDeSenha;

/// <summary>
/// Seção <c>Empresa</c>: nome da loja no e-mail de recuperação. Provisório — a story 4-1 (dados da empresa no módulo
/// Configuração) passa a ser a fonte, e o e-mail troca esta leitura pelo contrato de lá.
/// </summary>
public sealed class OpcoesDaEmpresa
{
    public const string Secao = "Empresa";

    public string Nome { get; set; } = "IMPA";
}
