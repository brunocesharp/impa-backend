using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Impa.Modules.Configuracao.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Notificacoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "configuracao");

            migrationBuilder.CreateTable(
                name: "inbox",
                schema: "configuracao",
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
                name: "notificacoes",
                schema: "configuracao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Titulo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Texto = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ReferenciaDaOrigem = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CriadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notificacoes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "outbox",
                schema: "configuracao",
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

            migrationBuilder.CreateTable(
                name: "notificacoes_destinatarios",
                schema: "configuracao",
                columns: table => new
                {
                    NotificacaoId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notificacoes_destinatarios", x => new { x.NotificacaoId, x.UsuarioId });
                    table.ForeignKey(
                        name: "FK_notificacoes_destinatarios_notificacoes_NotificacaoId",
                        column: x => x.NotificacaoId,
                        principalSchema: "configuracao",
                        principalTable: "notificacoes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notificacoes_lidas",
                schema: "configuracao",
                columns: table => new
                {
                    NotificacaoId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    LidaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notificacoes_lidas", x => new { x.NotificacaoId, x.UsuarioId });
                    table.ForeignKey(
                        name: "FK_notificacoes_lidas_notificacoes_NotificacaoId",
                        column: x => x.NotificacaoId,
                        principalSchema: "configuracao",
                        principalTable: "notificacoes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_notificacoes_CriadaEm",
                schema: "configuracao",
                table: "notificacoes",
                column: "CriadaEm");

            migrationBuilder.CreateIndex(
                name: "IX_notificacoes_destinatarios_UsuarioId",
                schema: "configuracao",
                table: "notificacoes_destinatarios",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_notificacoes_lidas_UsuarioId",
                schema: "configuracao",
                table: "notificacoes_lidas",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_chave_de_correlacao",
                schema: "configuracao",
                table: "outbox",
                column: "chave_de_correlacao");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_situacao_proxima_tentativa_em",
                schema: "configuracao",
                table: "outbox",
                columns: new[] { "situacao", "proxima_tentativa_em" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbox",
                schema: "configuracao");

            migrationBuilder.DropTable(
                name: "notificacoes_destinatarios",
                schema: "configuracao");

            migrationBuilder.DropTable(
                name: "notificacoes_lidas",
                schema: "configuracao");

            migrationBuilder.DropTable(
                name: "outbox",
                schema: "configuracao");

            migrationBuilder.DropTable(
                name: "notificacoes",
                schema: "configuracao");
        }
    }
}
