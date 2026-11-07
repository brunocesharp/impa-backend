using Impa.Modules.Pessoas.Application.Clientes;
using Impa.Modules.Pessoas.Application.Comum;
using Impa.Modules.Pessoas.Application.Fornecedores;
using Impa.Modules.Pessoas.Contracts;
using Impa.Shared;

namespace Impa.Modules.Pessoas.Application;

/// <summary>Implementação do contrato público <see cref="IPessoasConsultas"/> (PDV, Compras, Financeiro).</summary>
public sealed class PessoasConsultas(IClientesRepository clientes, IFornecedoresRepository fornecedores) : IPessoasConsultas
{
    public async Task<IReadOnlyList<ClienteResumo>> BuscarClientesAsync(string busca, int limite, CancellationToken ct)
    {
        var termos = TermosDeBusca.De(busca, incluirInativos: false, limite);
        if (termos.Vazia)
            return [];

        return (await clientes.BuscarAsync(termos, ct))
            .Select(c => new ClienteResumo(c.Id, c.Nome, c.Documento, c.Telefone, c.Email))
            .ToList();
    }

    public async Task<ClienteResumo?> ObterClienteAsync(Guid clienteId, CancellationToken ct) =>
        await clientes.ObterAsync(clienteId, ct) is { } c
            ? new ClienteResumo(c.Id, c.Nome, c.Documento, c.Telefone, c.Email)
            : null;

    public async Task<FornecedorResumo?> ObterFornecedorPorDocumentoAsync(string documento, CancellationToken ct)
    {
        var digitos = Documento.SomenteDigitos(documento);
        if (digitos.Length == 0)
            return null;

        return await fornecedores.ObterPorDocumentoAsync(digitos, null, ct) is { } f
            ? new FornecedorResumo(f.Id, f.RazaoSocial, f.NomeFantasia, f.Documento, f.Ativo)
            : null;
    }
}
