namespace Impa.Modules.Acesso.Contracts;

/// <summary>
/// Campo de resposta que só vai no JSON para quem tem a permissão (story 1.3, CA-1: o Operador de caixa não vê o custo).
/// Sem a permissão, o campo é omitido do JSON — não vem como <c>null</c>. Em records, use <c>[property: ExigePermissao(...)]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class ExigePermissaoAttribute(string permissao) : Attribute
{
    public string Permissao { get; } = permissao;
}
