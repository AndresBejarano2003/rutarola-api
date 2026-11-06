using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RutaRola.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Fase6Finanzas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_plantillas_mensaje_en_uso",
                table: "plantillas_mensaje");

            migrationBuilder.CreateIndex(
                name: "ix_plantillas_mensaje_en_uso",
                table: "plantillas_mensaje",
                columns: new[] { "id_programacion", "canal" },
                filter: "estado = 'EN_USO'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_plantillas_mensaje_en_uso",
                table: "plantillas_mensaje");

            migrationBuilder.CreateIndex(
                name: "ix_plantillas_mensaje_en_uso",
                table: "plantillas_mensaje",
                columns: new[] { "id_programacion", "canal" },
                unique: true,
                filter: "estado = 'EN_USO'");
        }
    }
}
