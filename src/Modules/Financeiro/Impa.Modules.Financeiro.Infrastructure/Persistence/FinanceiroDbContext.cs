using Impa.Modules.Financeiro.Domain;
using Microsoft.EntityFrameworkCore;

namespace Impa.Modules.Financeiro.Infrastructure.Persistence;

/// <summary>Contexto do módulo Financeiro. Grava só no schema <c>financeiro</c> (ADR-001 / ADR-012).</summary>
public sealed class FinanceiroDbContext(DbContextOptions<FinanceiroDbContext> options) : DbContext(options)
{
    public const string Schema = "financeiro";

    /// <summary>Propriedade sombra da versão da linha das contas (coluna <c>xmin</c>).</summary>
    public const string Versao = "Versao";

    public DbSet<CategoriaFinanceira> Categorias => Set<CategoriaFinanceira>();

    public DbSet<Conta> Contas => Set<Conta>();

    public DbSet<FormaDePagamentoFinanceira> FormasDePagamento => Set<FormaDePagamentoFinanceira>();

    public DbSet<Baixa> Baixas => Set<Baixa>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<CategoriaFinanceira>(b =>
        {
            b.ToTable("categorias");
            b.HasKey(c => c.Id);
            b.Property(c => c.Id).ValueGeneratedNever();
            b.Property(c => c.Nome).HasMaxLength(CategoriaFinanceira.NomeMaximo).IsRequired();
            b.Property(c => c.Ativa);
            b.Property(c => c.CriadaEm);
            // Nome único sem diferenciar maiúsculas: índice único sobre lower("Nome"), criado por SQL na migração
            // NomeDaCategoriaSemMaiusculas (o EF não mapeia índice por expressão).

            // RN-23.1: os exemplos da regra já vêm cadastrados (o administrador pode renomear, inativar ou excluir).
            b.HasData(CategoriasIniciais.Todas.Select(c => new
            {
                c.Id,
                c.Nome,
                Ativa = true,
                CriadaEm = CategoriasIniciais.CriadaEm,
            }));
        });

        modelBuilder.Entity<Conta>(b =>
        {
            b.ToTable("contas");
            b.HasKey(c => c.Id);
            b.Property(c => c.Id).ValueGeneratedNever();
            b.Property(c => c.Descricao).HasMaxLength(Conta.DescricaoMaximo).IsRequired();
            b.Property(c => c.Valor).HasPrecision(14, 2);
            b.Property(c => c.Vencimento);
            b.Property(c => c.FornecedorNome).HasMaxLength(Conta.FornecedorNomeMaximo);
            b.Property(c => c.FornecedorDocumento).HasMaxLength(Conta.FornecedorDocumentoMaximo);
            b.Property(c => c.NumeroDocumento).HasMaxLength(Conta.NumeroDocumentoMaximo);
            b.Property(c => c.Observacao).HasMaxLength(Conta.ObservacaoMaximo);
            b.Property(c => c.CriadaEm);
            b.Property(c => c.ValorPago).HasPrecision(14, 2);
            // Versão da linha (xmin do Postgres): cada baixa ou estorno atualiza o ValorPago, e duas operações ao mesmo
            // tempo na mesma conta não passam do saldo (a segunda recebe 409).
            b.Property<uint>(Versao).IsRowVersion();
            b.Ignore(c => c.Saldo);
            b.Ignore(c => c.TemPagamento);
            b.Ignore(c => c.Pagamento);

            // RN-23.5: as baixas da conta. Sem pagamento, a conta pode ser excluída com as baixas estornadas (RN-23.7).
            b.HasMany(c => c.Baixas).WithOne().HasForeignKey(x => x.ContaId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(c => c.Baixas).UsePropertyAccessMode(PropertyAccessMode.Field);

            // RN-23.1: categoria com contas não é excluída (a FK recusa mesmo numa corrida).
            b.HasOne<CategoriaFinanceira>().WithMany().HasForeignKey(c => c.CategoriaId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(c => c.Vencimento);
            b.HasIndex(c => c.FornecedorId);
            b.HasIndex(c => c.ParcelamentoId);
            b.ToTable(t =>
            {
                t.HasCheckConstraint("ck_contas_valor_positivo", "\"Valor\" > 0");
                t.HasCheckConstraint("ck_contas_valor_pago", "\"ValorPago\" >= 0 AND \"ValorPago\" <= \"Valor\"");
            });
        });

        modelBuilder.Entity<FormaDePagamentoFinanceira>(b =>
        {
            b.ToTable("formas_pagamento");
            b.HasKey(f => f.Id);
            b.Property(f => f.Id).ValueGeneratedNever();
            b.Property(f => f.Nome).HasMaxLength(FormaDePagamentoFinanceira.NomeMaximo).IsRequired();
            b.Property(f => f.Ativa);
            b.Property(f => f.CriadaEm);
            // Nome único sem diferenciar maiúsculas: índice único sobre lower("Nome"), criado por SQL na migração
            // BaixasEFormasDePagamento (o EF não mapeia índice por expressão).

            // RN-23.4: os exemplos da regra já vêm cadastrados (o administrador pode renomear, inativar ou excluir).
            b.HasData(FormasDePagamentoIniciais.Todas.Select(f => new
            {
                f.Id,
                f.Nome,
                Ativa = true,
                CriadaEm = FormasDePagamentoIniciais.CriadaEm,
            }));
        });

        modelBuilder.Entity<Baixa>(b =>
        {
            b.ToTable("baixas");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Data);
            b.Property(x => x.Valor).HasPrecision(14, 2);
            b.Property(x => x.RegistradaEm);
            b.Property(x => x.EstornadaEm);
            b.Property(x => x.EstornadaPorNome).HasMaxLength(Baixa.UsuarioNomeMaximo);
            b.Property(x => x.MotivoDoEstorno).HasMaxLength(Baixa.MotivoMaximo);
            b.Ignore(x => x.Estornada);

            // A baixa guarda a forma usada: forma com pagamentos não é excluída, só inativada.
            b.HasOne<FormaDePagamentoFinanceira>().WithMany().HasForeignKey(x => x.FormaDePagamentoId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.ContaId);
            b.HasIndex(x => x.FormaDePagamentoId);
            b.ToTable(t => t.HasCheckConstraint("ck_baixas_valor_positivo", "\"Valor\" > 0"));
        });
    }
}

internal static class IndicesUnicos
{
    public const string NomeDaCategoria = "ux_categorias_nome";
    public const string NomeDaFormaDePagamento = "ux_formas_pagamento_nome";
}

internal static class ChavesEstrangeiras
{
    /// <summary>Nome gerado pelo EF na migração BaixasEFormasDePagamento.</summary>
    public const string FormaDePagamentoDaBaixa = "FK_baixas_formas_pagamento_FormaDePagamentoId";
}

/// <summary>Os exemplos da RN-23.4, com ids fixos (seed da migração).</summary>
internal static class FormasDePagamentoIniciais
{
    public static readonly DateTimeOffset CriadaEm = new(2026, 11, 8, 0, 0, 0, TimeSpan.Zero);

    public static readonly (Guid Id, string Nome)[] Todas =
    [
        (new Guid("0199a000-0000-7000-8000-000000000101"), "Dinheiro"),
        (new Guid("0199a000-0000-7000-8000-000000000102"), "Pix"),
        (new Guid("0199a000-0000-7000-8000-000000000103"), "Boleto"),
        (new Guid("0199a000-0000-7000-8000-000000000104"), "Transferência"),
    ];
}

/// <summary>Os exemplos da RN-23.1, com ids fixos (seed da migração).</summary>
internal static class CategoriasIniciais
{
    public static readonly DateTimeOffset CriadaEm = new(2026, 11, 7, 0, 0, 0, TimeSpan.Zero);

    public static readonly (Guid Id, string Nome)[] Todas =
    [
        (new Guid("0199a000-0000-7000-8000-000000000001"), "Compra de mercadorias"),
        (new Guid("0199a000-0000-7000-8000-000000000002"), "Aluguel"),
        (new Guid("0199a000-0000-7000-8000-000000000003"), "Energia"),
        (new Guid("0199a000-0000-7000-8000-000000000004"), "Internet"),
        (new Guid("0199a000-0000-7000-8000-000000000005"), "Impostos"),
    ];
}
