using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Impa.Modules.Acesso.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GestaoDeUsuarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "TrocaDeSenhaObrigatoria",
                schema: "acesso",
                table: "usuarios",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TrocaDeSenhaObrigatoria",
                schema: "acesso",
                table: "usuarios");
        }
    }
}
