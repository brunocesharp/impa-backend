using System.Reflection;
using Impa.Shared.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Impa.Architecture.Tests;

/// <summary>
/// Story 1-7, T7 (CA-4) — ADR-012, regra 1: um módulo grava só no próprio schema.
/// Todo <see cref="DbContext"/> de <c>Impa.Modules.{Modulo}.Infrastructure</c> mapeia apenas o schema
/// <c>{modulo}</c> (minúsculo), inclusive Outbox e Inbox. A proibição de referenciar a Infrastructure de outro
/// módulo está em <see cref="ModuleDependencyTests"/>.
/// </summary>
public class DbContextSchemaTests
{
    private sealed class ContextoQueInvadeOutroSchema(DbContextOptions<ContextoQueInvadeOutroSchema> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("vendas");
            modelBuilder.Entity<Venda>().ToTable("vendas");
            modelBuilder.Entity<Saldo>().ToTable("saldos", "estoque"); // grava no schema do Estoque
            modelBuilder.MapearOutbox("estoque");
        }
    }

    private sealed class ContextoCorreto(DbContextOptions<ContextoCorreto> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("vendas");
            modelBuilder.Entity<Venda>().ToTable("vendas");
            modelBuilder.MapearOutbox("vendas");
            modelBuilder.MapearInbox("vendas");
        }
    }

    private sealed record Venda(Guid Id);

    private sealed record Saldo(Guid Id);

    [Fact]
    public void Contexto_que_mapeia_tabela_de_outro_schema_gera_violacao()
    {
        var violacoes = DbContextSchemaRules.Violations(typeof(ContextoQueInvadeOutroSchema), "vendas");

        Assert.Equal(2, violacoes.Count);
        Assert.Contains(violacoes, v => v.Contains("estoque.saldos"));
        Assert.Contains(violacoes, v => v.Contains($"estoque.{TabelasDeMensageria.Outbox}"));
    }

    [Fact]
    public void Contexto_que_mapeia_so_o_proprio_schema_nao_gera_violacao()
    {
        Assert.Empty(DbContextSchemaRules.Violations(typeof(ContextoCorreto), "vendas"));
    }

    [Fact]
    public void Contextos_dos_modulos_mapeiam_so_o_proprio_schema()
    {
        var contextos = Directory
            .GetFiles(AppContext.BaseDirectory, "Impa.Modules.*.Infrastructure.dll")
            .Select(Assembly.LoadFrom)
            .SelectMany(a => a.GetTypes()
                .Where(t => typeof(DbContext).IsAssignableFrom(t) && !t.IsAbstract)
                .Select(t => (Contexto: t, Schema: DbContextSchemaRules.SchemaDoModulo(a.GetName().Name!))))
            .ToList();

        Assert.Contains(contextos, c => c.Contexto.Name == "AcessoDbContext");

        var violacoes = contextos.SelectMany(c => DbContextSchemaRules.Violations(c.Contexto, c.Schema)).ToList();
        Assert.True(violacoes.Count == 0, string.Join(Environment.NewLine, violacoes));
    }
}
