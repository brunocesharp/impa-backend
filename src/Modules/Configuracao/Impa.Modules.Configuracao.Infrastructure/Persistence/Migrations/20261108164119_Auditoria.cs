using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Impa.Modules.Configuracao.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Auditoria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "auditoria",
                schema: "configuracao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OcorreuEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RegistradoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioNome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Modulo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Entidade = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntidadeId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Operacao = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    campos = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auditoria", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_auditoria_Modulo_Entidade",
                schema: "configuracao",
                table: "auditoria",
                columns: new[] { "Modulo", "Entidade" });

            migrationBuilder.CreateIndex(
                name: "IX_auditoria_OcorreuEm",
                schema: "configuracao",
                table: "auditoria",
                column: "OcorreuEm");

            migrationBuilder.CreateIndex(
                name: "IX_auditoria_UsuarioId",
                schema: "configuracao",
                table: "auditoria",
                column: "UsuarioId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auditoria",
                schema: "configuracao");
        }
    }
}
