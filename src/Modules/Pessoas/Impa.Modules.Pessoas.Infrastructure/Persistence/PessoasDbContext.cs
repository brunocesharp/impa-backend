using Impa.Modules.Pessoas.Domain;
using Microsoft.EntityFrameworkCore;

namespace Impa.Modules.Pessoas.Infrastructure.Persistence;

/// <summary>Contexto do módulo Pessoas. Grava só no schema <c>pessoas</c> (ADR-001 / ADR-012).</summary>
public sealed class PessoasDbContext(DbContextOptions<PessoasDbContext> options) : DbContext(options)
{
    public const string Schema = "pessoas";

    public DbSet<Cliente> Clientes => Set<Cliente>();

    public DbSet<Fornecedor> Fornecedores => Set<Fornecedor>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Cliente>(b =>
        {
            b.ToTable("clientes");
            b.HasKey(c => c.Id);
            b.Property(c => c.Id).ValueGeneratedNever();
            b.Property(c => c.Nome).HasMaxLength(Cliente.NomeMaximo).IsRequired();
            b.Property(c => c.Documento).HasMaxLength(Impa.Shared.Documento.DigitosCnpj);
            b.Property(c => c.Telefone).HasMaxLength(Cliente.TelefoneMaximo);
            b.Property(c => c.Email).HasMaxLength(Cliente.EmailMaximo);
            b.Property(c => c.Endereco).HasMaxLength(Cliente.EnderecoMaximo);
            b.Property(c => c.Observacoes).HasMaxLength(Cliente.ObservacoesMaximo);
            b.Property(c => c.Ativo);
            b.Property(c => c.CriadoEm);

            // RN-14.1: documento único entre os clientes, só quando informado (índice único parcial).
            b.HasIndex(c => c.Documento).IsUnique().HasFilter("\"Documento\" IS NOT NULL")
                .HasDatabaseName(IndicesUnicos.DocumentoDoCliente);
            b.HasIndex(c => c.Telefone);
        });

        modelBuilder.Entity<Fornecedor>(b =>
        {
            b.ToTable("fornecedores");
            b.HasKey(f => f.Id);
            b.Property(f => f.Id).ValueGeneratedNever();
            b.Property(f => f.RazaoSocial).HasMaxLength(Fornecedor.RazaoSocialMaximo).IsRequired();
            b.Property(f => f.Documento).HasMaxLength(Impa.Shared.Documento.DigitosCnpj).IsRequired();
            b.Property(f => f.NomeFantasia).HasMaxLength(Fornecedor.NomeFantasiaMaximo);
            b.Property(f => f.Telefone).HasMaxLength(Fornecedor.TelefoneMaximo);
            b.Property(f => f.Email).HasMaxLength(Fornecedor.EmailMaximo);
            b.Property(f => f.Contato).HasMaxLength(Fornecedor.ContatoMaximo);
            b.Property(f => f.Ativo);
            b.Property(f => f.CriadoEm);

            // RN-14.2: documento obrigatório e único entre os fornecedores.
            b.HasIndex(f => f.Documento).IsUnique().HasDatabaseName(IndicesUnicos.DocumentoDoFornecedor);
        });
    }
}

internal static class IndicesUnicos
{
    public const string DocumentoDoCliente = "ux_clientes_documento";
    public const string DocumentoDoFornecedor = "ux_fornecedores_documento";
}
