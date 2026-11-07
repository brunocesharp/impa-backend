namespace Impa.Modules.Pessoas.Contracts;

/// <summary>Cliente como os outros módulos o veem (ex.: o PDV). <c>Documento</c> e <c>Telefone</c> só com dígitos.</summary>
public sealed record ClienteResumo(Guid Id, string Nome, string? Documento, string? Telefone, string? Email)
{
    // RN-14.6: CPF, telefone e e-mail fora de qualquer log.
    public override string ToString() => $"{nameof(ClienteResumo)} {{ Id = {Id} }}";
}

/// <summary>Fornecedor como os outros módulos o veem (ex.: Compras). <c>Documento</c> só com dígitos.</summary>
public sealed record FornecedorResumo(Guid Id, string RazaoSocial, string? NomeFantasia, string Documento, bool Ativo)
{
    public override string ToString() => $"{nameof(FornecedorResumo)} {{ Id = {Id} }}";
}

/// <summary>
/// Contrato público do módulo Pessoas para PDV (épico 6), Compras (épico 12) e Financeiro (épico 11).
/// Os dados pessoais devolvidos (RN-14.6) só podem ir para a tela de usuários com acesso a clientes.
/// </summary>
public interface IPessoasConsultas
{
    /// <summary>
    /// Clientes <b>ativos</b> pela busca da RN-14.3: nome (partes, sem diferenciar maiúsculas nem acentos, em qualquer
    /// ordem), CPF/CNPJ ou telefone (com ou sem máscara).
    /// </summary>
    Task<IReadOnlyList<ClienteResumo>> BuscarClientesAsync(string busca, int limite, CancellationToken ct);

    Task<ClienteResumo?> ObterClienteAsync(Guid clienteId, CancellationToken ct);

    /// <summary>Fornecedor (ativo ou não) pelo CNPJ/CPF, com ou sem máscara; nulo se não houver.</summary>
    Task<FornecedorResumo?> ObterFornecedorPorDocumentoAsync(string documento, CancellationToken ct);
}
