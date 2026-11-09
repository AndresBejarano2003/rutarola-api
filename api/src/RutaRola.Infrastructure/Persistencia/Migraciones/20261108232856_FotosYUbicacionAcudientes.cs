using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RutaRola.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class FotosYUbicacionAcudientes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "foto",
                table: "usuarios",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "foto",
                table: "estudiantes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "foto",
                table: "empleados",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "foto",
                table: "acudientes",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "foto",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "foto",
                table: "estudiantes");

            migrationBuilder.DropColumn(
                name: "foto",
                table: "empleados");

            migrationBuilder.DropColumn(
                name: "foto",
                table: "acudientes");
        }
    }
}
