using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Impa.Modules.Acesso.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Terminais : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "terminais",
                schema: "acesso",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    NomeNormalizado = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    TokenHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    CriadoPor = table.Column<Guid>(type: "uuid", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevogadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_terminais", x => x.Id);
                    table.ForeignKey(
                        name: "FK_terminais_usuarios_CriadoPor",
                        column: x => x.CriadoPor,
                        principalSchema: "acesso",
                        principalTable: "usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_terminais_CriadoPor",
                schema: "acesso",
                table: "terminais",
                column: "CriadoPor");

            migrationBuilder.CreateIndex(
                name: "IX_terminais_NomeNormalizado",
                schema: "acesso",
                table: "terminais",
                column: "NomeNormalizado",
                unique: true,
                filter: "\"RevogadoEm\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_terminais_TokenHash",
                schema: "acesso",
                table: "terminais",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "terminais",
                schema: "acesso");
        }
    }
}
