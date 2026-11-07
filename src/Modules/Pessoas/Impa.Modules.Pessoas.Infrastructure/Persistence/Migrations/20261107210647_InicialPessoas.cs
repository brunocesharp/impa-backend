using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Impa.Modules.Pessoas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InicialPessoas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "pessoas");

            migrationBuilder.CreateTable(
                name: "clientes",
                schema: "pessoas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Documento = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    Telefone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Endereco = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Observacoes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clientes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "fornecedores",
                schema: "pessoas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RazaoSocial = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Documento = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    NomeFantasia = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Telefone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Contato = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fornecedores", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_clientes_Telefone",
                schema: "pessoas",
                table: "clientes",
                column: "Telefone");

            migrationBuilder.CreateIndex(
                name: "ux_clientes_documento",
                schema: "pessoas",
                table: "clientes",
                column: "Documento",
                unique: true,
                filter: "\"Documento\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_fornecedores_documento",
                schema: "pessoas",
                table: "fornecedores",
                column: "Documento",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "clientes",
                schema: "pessoas");

            migrationBuilder.DropTable(
                name: "fornecedores",
                schema: "pessoas");
        }
    }
}
