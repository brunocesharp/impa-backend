using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Impa.Modules.Financeiro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NomeDaCategoriaSemMaiusculas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_categorias_nome",
                schema: "financeiro",
                table: "categorias");

            // RN-23.1: nome único sem diferenciar maiúsculas também no banco ("Energia" e "energia" não convivem).
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ux_categorias_nome ON financeiro.categorias (lower(\"Nome\"));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX financeiro.ux_categorias_nome;");

            migrationBuilder.CreateIndex(
                name: "ux_categorias_nome",
                schema: "financeiro",
                table: "categorias",
                column: "Nome",
                unique: true);
        }
    }
}
