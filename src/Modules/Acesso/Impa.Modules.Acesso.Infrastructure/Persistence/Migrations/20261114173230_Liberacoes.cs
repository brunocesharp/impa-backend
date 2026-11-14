using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Impa.Modules.Acesso.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Liberacoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "liberacoes",
                schema: "acesso",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    SessaoId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    LiberadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Permissao = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CriadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiraEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UsadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Operacao = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_liberacoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_liberacoes_usuarios_LiberadorId",
                        column: x => x.LiberadorId,
                        principalSchema: "acesso",
                        principalTable: "usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_liberacoes_usuarios_OperadorId",
                        column: x => x.OperadorId,
                        principalSchema: "acesso",
                        principalTable: "usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_liberacoes_LiberadorId",
                schema: "acesso",
                table: "liberacoes",
                column: "LiberadorId");

            migrationBuilder.CreateIndex(
                name: "IX_liberacoes_OperadorId",
                schema: "acesso",
                table: "liberacoes",
                column: "OperadorId");

            migrationBuilder.CreateIndex(
                name: "IX_liberacoes_TokenHash",
                schema: "acesso",
                table: "liberacoes",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "liberacoes",
                schema: "acesso");
        }
    }
}
