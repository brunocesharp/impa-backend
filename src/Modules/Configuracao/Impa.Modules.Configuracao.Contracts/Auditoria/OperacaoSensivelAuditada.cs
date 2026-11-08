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
/// Não mande senha, CSC nem dados pessoais (CPF, telefone, e-mail, endereço — RN-14.6, RN-15.6): marque o campo com
/// <see cref="CampoAlterado.Sensivel"/> para registrar só que ele mudou. Os nomes conhecidos (ex.: <c>Senha</c>,
/// <c>Csc</c>, <c>Cpf</c>) são recusados pelo Configuração mesmo sem a marca.
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

/// <summary>Um campo alterado: valor antes e depois (nulo quando não havia). <see cref="Sensivel"/>: só o nome é gravado.</summary>
public sealed record CampoAlterado(string Campo, string? Antes, string? Depois, bool Sensivel = false);
