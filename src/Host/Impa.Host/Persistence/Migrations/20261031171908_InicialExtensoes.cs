using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Impa.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InicialExtensoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:public.pg_trgm", ",,")
                .Annotation("Npgsql:PostgresExtension:public.unaccent", ",,");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
