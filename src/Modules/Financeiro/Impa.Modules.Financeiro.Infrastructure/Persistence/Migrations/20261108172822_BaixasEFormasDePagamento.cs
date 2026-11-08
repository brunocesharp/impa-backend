using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Impa.Modules.Financeiro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BaixasEFormasDePagamento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ValorPago",
                schema: "financeiro",
                table: "contas",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            // A versão da linha das contas é a coluna de sistema xmin do Postgres: não se cria coluna.

            migrationBuilder.CreateTable(
                name: "formas_pagamento",
                schema: "financeiro",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Ativa = table.Column<bool>(type: "boolean", nullable: false),
                    CriadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_formas_pagamento", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "baixas",
                schema: "financeiro",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Data = table.Column<DateOnly>(type: "date", nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    FormaDePagamentoId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegistradaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EstornadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EstornadaPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    EstornadaPorNome = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    MotivoDoEstorno = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_baixas", x => x.Id);
                    table.CheckConstraint("ck_baixas_valor_positivo", "\"Valor\" > 0");
                    table.ForeignKey(
                        name: "FK_baixas_contas_ContaId",
                        column: x => x.ContaId,
                        principalSchema: "financeiro",
                        principalTable: "contas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_baixas_formas_pagamento_FormaDePagamentoId",
                        column: x => x.FormaDePagamentoId,
                        principalSchema: "financeiro",
                        principalTable: "formas_pagamento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // RN-23.4: nome único sem diferenciar maiúsculas também no banco ("Pix" e "pix" não convivem).
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ux_formas_pagamento_nome ON financeiro.formas_pagamento (lower(\"Nome\"));");

            migrationBuilder.InsertData(
                schema: "financeiro",
                table: "formas_pagamento",
                columns: new[] { "Id", "Ativa", "CriadaEm", "Nome" },
                values: new object[,]
                {
                    { new Guid("0199a000-0000-7000-8000-000000000101"), true, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Dinheiro" },
                    { new Guid("0199a000-0000-7000-8000-000000000102"), true, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Pix" },
                    { new Guid("0199a000-0000-7000-8000-000000000103"), true, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Boleto" },
                    { new Guid("0199a000-0000-7000-8000-000000000104"), true, new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Transferência" }
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_contas_valor_pago",
                schema: "financeiro",
                table: "contas",
                sql: "\"ValorPago\" >= 0 AND \"ValorPago\" <= \"Valor\"");

            migrationBuilder.CreateIndex(
                name: "IX_baixas_ContaId",
                schema: "financeiro",
                table: "baixas",
                column: "ContaId");

            migrationBuilder.CreateIndex(
                name: "IX_baixas_FormaDePagamentoId",
                schema: "financeiro",
                table: "baixas",
                column: "FormaDePagamentoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "baixas",
                schema: "financeiro");

            migrationBuilder.DropTable(
                name: "formas_pagamento",
                schema: "financeiro");

            migrationBuilder.DropCheckConstraint(
                name: "ck_contas_valor_pago",
                schema: "financeiro",
                table: "contas");

            migrationBuilder.DropColumn(
                name: "ValorPago",
                schema: "financeiro",
                table: "contas");

        }
    }
}
