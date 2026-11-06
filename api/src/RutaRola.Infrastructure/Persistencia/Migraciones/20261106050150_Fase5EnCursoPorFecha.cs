using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RutaRola.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Fase5EnCursoPorFecha : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_viajes_ruta_en_curso",
                table: "viajes_ruta");

            migrationBuilder.CreateIndex(
                name: "ix_viajes_ruta_en_curso",
                table: "viajes_ruta",
                columns: new[] { "id_asignacion", "fecha_viaje" },
                unique: true,
                filter: "estado = 'EN_CURSO'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_viajes_ruta_en_curso",
                table: "viajes_ruta");

            migrationBuilder.CreateIndex(
                name: "ix_viajes_ruta_en_curso",
                table: "viajes_ruta",
                column: "id_asignacion",
                unique: true,
                filter: "estado = 'EN_CURSO'");
        }
    }
}
