using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Impa.Modules.Acesso.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecuperacaoDeSenha : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inbox",
                schema: "acesso",
                columns: table => new
                {
                    evento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    handler = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    processado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox", x => new { x.evento_id, x.handler });
                });

            migrationBuilder.CreateTable(
                name: "outbox",
                schema: "acesso",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    versao = table.Column<int>(type: "integer", nullable: false),
                    chave_de_correlacao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    conteudo = table.Column<string>(type: "jsonb", nullable: false),
                    ocorreu_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    situacao = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tentativas = table.Column<int>(type: "integer", nullable: false),
                    proxima_tentativa_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ultimo_erro = table.Column<string>(type: "text", nullable: true),
                    entregue_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_chave_de_correlacao",
                schema: "acesso",
                table: "outbox",
                column: "chave_de_correlacao");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_situacao_proxima_tentativa_em",
                schema: "acesso",
                table: "outbox",
                columns: new[] { "situacao", "proxima_tentativa_em" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbox",
                schema: "acesso");

            migrationBuilder.DropTable(
                name: "outbox",
                schema: "acesso");
        }
    }
}
