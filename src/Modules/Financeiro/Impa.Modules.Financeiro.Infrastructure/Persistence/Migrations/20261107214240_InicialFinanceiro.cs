using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Impa.Modules.Financeiro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InicialFinanceiro : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "financeiro");

            migrationBuilder.CreateTable(
                name: "categorias",
                schema: "financeiro",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Ativa = table.Column<bool>(type: "boolean", nullable: false),
                    CriadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_categorias", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "contas",
                schema: "financeiro",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Descricao = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    CategoriaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    Vencimento = table.Column<DateOnly>(type: "date", nullable: false),
                    FornecedorId = table.Column<Guid>(type: "uuid", nullable: true),
                    FornecedorNome = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    FornecedorDocumento = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    NumeroDocumento = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Observacao = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Parcela = table.Column<int>(type: "integer", nullable: false),
                    TotalDeParcelas = table.Column<int>(type: "integer", nullable: false),
                    ParcelamentoId = table.Column<Guid>(type: "uuid", nullable: true),
                    CriadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contas", x => x.Id);
                    table.CheckConstraint("ck_contas_valor_positivo", "\"Valor\" > 0");
                    table.ForeignKey(
                        name: "FK_contas_categorias_CategoriaId",
                        column: x => x.CategoriaId,
                        principalSchema: "financeiro",
                        principalTable: "categorias",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "financeiro",
                table: "categorias",
                columns: new[] { "Id", "Ativa", "CriadaEm", "Nome" },
                values: new object[,]
                {
                    { new Guid("0199a000-0000-7000-8000-000000000001"), true, new DateTimeOffset(new DateTime(2026, 11, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Compra de mercadorias" },
                    { new Guid("0199a000-0000-7000-8000-000000000002"), true, new DateTimeOffset(new DateTime(2026, 11, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Aluguel" },
                    { new Guid("0199a000-0000-7000-8000-000000000003"), true, new DateTimeOffset(new DateTime(2026, 11, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Energia" },
                    { new Guid("0199a000-0000-7000-8000-000000000004"), true, new DateTimeOffset(new DateTime(2026, 11, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Internet" },
                    { new Guid("0199a000-0000-7000-8000-000000000005"), true, new DateTimeOffset(new DateTime(2026, 11, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Impostos" }
                });

            migrationBuilder.CreateIndex(
                name: "ux_categorias_nome",
                schema: "financeiro",
                table: "categorias",
                column: "Nome",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_contas_CategoriaId",
                schema: "financeiro",
                table: "contas",
                column: "CategoriaId");

            migrationBuilder.CreateIndex(
                name: "IX_contas_FornecedorId",
                schema: "financeiro",
                table: "contas",
                column: "FornecedorId");

            migrationBuilder.CreateIndex(
                name: "IX_contas_ParcelamentoId",
                schema: "financeiro",
                table: "contas",
                column: "ParcelamentoId");

            migrationBuilder.CreateIndex(
                name: "IX_contas_Vencimento",
                schema: "financeiro",
                table: "contas",
                column: "Vencimento");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contas",
                schema: "financeiro");

            migrationBuilder.DropTable(
                name: "categorias",
                schema: "financeiro");
        }
    }
}
