using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Impa.Modules.Acesso.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BloqueioESessoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Ativo",
                schema: "acesso",
                table: "usuarios",
                type: "boolean",
                nullable: false,
                defaultValue: true); // usuários já existentes continuam ativos

            // RN-1.2: bloqueio por tentativas ligado para todos, inclusive os criados antes desta versão.
            migrationBuilder.Sql("""UPDATE acesso.usuarios SET "LockoutEnabled" = true;""");

            migrationBuilder.CreateTable(
                name: "sessoes",
                schema: "acesso",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiraEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevogadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Tiquete = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sessoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sessoes_usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalSchema: "acesso",
                        principalTable: "usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sessoes_UsuarioId",
                schema: "acesso",
                table: "sessoes",
                column: "UsuarioId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sessoes",
                schema: "acesso");

            migrationBuilder.DropColumn(
                name: "Ativo",
                schema: "acesso",
                table: "usuarios");
        }
    }
}
