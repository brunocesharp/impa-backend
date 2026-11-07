using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Impa.Modules.Acesso.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PerfisEPermissoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Protegido",
                schema: "acesso",
                table: "perfis",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // RN-1.6: o Administrador criado pela story 1-1 passa a ser protegido (o seed também garante).
            migrationBuilder.Sql("""UPDATE acesso.perfis SET "Protegido" = TRUE WHERE "NormalizedName" = 'ADMINISTRADOR';""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Protegido",
                schema: "acesso",
                table: "perfis");
        }
    }
}
