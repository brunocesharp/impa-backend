using Impa.Shared.Messaging;

namespace Impa.Modules.Configuracao.Contracts.Auditoria;

/// <summary>
/// Uma operação sensível foi feita (M1.6, story 4.4): alteração de preço, custo, tributação, estoque, caixa ou
/// configuração fiscal. O módulo de origem publica este evento <b>no próprio Outbox</b>, na mesma transação da
/// alteração (ADR-011), e nunca grava no schema <c>configuracao</c> (ADR-012); o Configuração o grava uma vez só
/// (Inbox) na auditoria. <see cref="IntegrationEvent.OcorreuEm"/> é a data da operação.
/// <code>
/// publicador.Publicar(new OperacaoSensivelAuditada(
///     usuarioId, usuarioNome, "Catalogo", "Item", item.Id.ToString(), "custo.alterado",
///     [new CampoAlterado("Custo", "10,00", "12,50")]));
/// </code>
/// Senha, CSC e dados pessoais (CPF, telefone, e-mail, endereço — RN-14.6, RN-15.6) nunca saem do módulo de origem:
/// o <see cref="CampoAlterado"/> marcado com <see cref="CampoAlterado.Sensivel"/> ou com nome conhecido
/// (<see cref="CamposSensiveis"/>, ex.: <c>Senha</c>, <c>Csc</c>, <c>Cpf</c>) é construído já sem <c>Antes</c> e
/// <c>Depois</c>, então o valor não chega ao Outbox nem à auditoria; fica registrado só que o campo mudou. O
/// Configuração recusa o valor de novo ao gravar (segunda barreira).
/// </summary>
/// <param name="UsuarioId">Quem fez a operação (usuário da sessão).</param>
/// <param name="UsuarioNome">Nome de exibição do usuário no momento da operação (aparece na consulta).</param>
/// <param name="Modulo">Módulo de origem (ex.: <c>Catalogo</c>, <c>Fiscal</c>, <c>Caixa</c>, <c>Estoque</c>).</param>
/// <param name="Entidade">O que foi alterado (ex.: <c>Item</c>, <c>PerfilDeTributacao</c>, <c>ConfiguracaoFiscal</c>).</param>
/// <param name="EntidadeId">Identificador do dado alterado.</param>
/// <param name="Operacao">Operação estável (ex.: <c>custo.alterado</c>, <c>perfil-de-tributacao.alterado</c>).</param>
/// <param name="Campos">Só os campos alterados, com o valor antes e depois já formatados para leitura.</param>
[EventoDeIntegracao(TipoDoEvento, 1)]
public sealed record OperacaoSensivelAuditada(
    Guid UsuarioId,
    string UsuarioNome,
    string Modulo,
    string Entidade,
    string EntidadeId,
    string Operacao,
    IReadOnlyList<CampoAlterado> Campos) : IntegrationEvent
{
    public const string TipoDoEvento = "auditoria.operacao-sensivel";

    public override string? ChaveDeCorrelacao => $"{Entidade}:{EntidadeId}";
}

/// <summary>
/// Um campo alterado: valor antes e depois (nulo quando não havia), já formatados para leitura. Campo sensível
/// (<paramref name="Sensivel"/> ou nome em <see cref="CamposSensiveis"/>) fica só com o nome: <see cref="Antes"/> e
/// <see cref="Depois"/> são descartados aqui, na construção, antes de o evento ir para o Outbox (CA-4). As
/// propriedades não têm <c>init</c>, para um <c>with</c> não recolocar o valor.
/// </summary>
/// <param name="Campo">Nome do campo (ex.: <c>Custo</c>, <c>Cfop</c>, <c>Csc</c>).</param>
/// <param name="Antes">Valor antes da alteração (descartado se o campo for sensível).</param>
/// <param name="Depois">Valor depois da alteração (descartado se o campo for sensível).</param>
/// <param name="Sensivel">Marca do módulo de origem: só o nome do campo é registrado.</param>
public sealed record CampoAlterado
{
    public CampoAlterado(string Campo, string? Antes, string? Depois, bool Sensivel = false)
    {
        this.Campo = Campo;
        this.Sensivel = Sensivel || CamposSensiveis.EhSensivel(Campo);
        this.Antes = this.Sensivel ? null : Antes;
        this.Depois = this.Sensivel ? null : Depois;
    }

    public string Campo { get; }

    public string? Antes { get; }

    public string? Depois { get; }

    /// <summary>Verdadeiro quando o campo é sensível (marcado ou nome conhecido): sem valores.</summary>
    public bool Sensivel { get; }
}
