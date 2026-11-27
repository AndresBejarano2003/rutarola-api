using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RutaRola.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class RutasVariosColegios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "direccion_colegio",
                table: "paradas_viaje",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "id_colegio",
                table: "paradas_viaje",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "nombre_colegio",
                table: "paradas_viaje",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "rutas_colegios",
                columns: table => new
                {
                    id_ruta_colegio = table.Column<Guid>(type: "uuid", nullable: false),
                    id_ruta = table.Column<Guid>(type: "uuid", nullable: false),
                    id_colegio = table.Column<Guid>(type: "uuid", nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rutas_colegios", x => x.id_ruta_colegio);
                    table.ForeignKey(
                        name: "fk_rutas_colegios_colegios_id_colegio",
                        column: x => x.id_colegio,
                        principalTable: "colegios",
                        principalColumn: "id_colegio",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rutas_colegios_rutas_id_ruta",
                        column: x => x.id_ruta,
                        principalTable: "rutas",
                        principalColumn: "id_ruta",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_rutas_colegios_id_colegio",
                table: "rutas_colegios",
                column: "id_colegio");

            migrationBuilder.CreateIndex(
                name: "ix_rutas_colegios_id_ruta_id_colegio",
                table: "rutas_colegios",
                columns: new[] { "id_ruta", "id_colegio" },
                unique: true);

            // Cada ruta existente queda con su colegio como único colegio, y los recorridos anteriores guardan en cada
            // estudiante el colegio que tenían (el de la ruta).
            migrationBuilder.Sql("""
                INSERT INTO rutas_colegios (id_ruta_colegio, id_ruta, id_colegio, orden)
                SELECT gen_random_uuid(), id_ruta, id_colegio, 1 FROM rutas;
                UPDATE paradas_viaje p
                SET id_colegio = r.id_colegio, nombre_colegio = v.nombre_colegio, direccion_colegio = v.direccion_colegio
                FROM viajes_ruta v JOIN rutas r ON r.id_ruta = v.id_ruta
                WHERE p.id_viaje = v.id_viaje;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_rutas_colegios_id_colegio",
                table: "rutas");

            migrationBuilder.DropIndex(
                name: "ix_rutas_id_colegio",
                table: "rutas");

            migrationBuilder.DropColumn(
                name: "id_colegio",
                table: "rutas");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "id_colegio",
                table: "rutas",
                type: "uuid",
                nullable: true);

            // Vuelve al primer colegio de cada ruta.
            migrationBuilder.Sql("""
                UPDATE rutas r SET id_colegio = (
                    SELECT rc.id_colegio FROM rutas_colegios rc WHERE rc.id_ruta = r.id_ruta ORDER BY rc.orden LIMIT 1);
                ALTER TABLE rutas ALTER COLUMN id_colegio SET NOT NULL;
                """);

            migrationBuilder.DropTable(
                name: "rutas_colegios");

            migrationBuilder.DropColumn(
                name: "direccion_colegio",
                table: "paradas_viaje");

            migrationBuilder.DropColumn(
                name: "id_colegio",
                table: "paradas_viaje");

            migrationBuilder.DropColumn(
                name: "nombre_colegio",
                table: "paradas_viaje");

            migrationBuilder.CreateIndex(
                name: "ix_rutas_id_colegio",
                table: "rutas",
                column: "id_colegio");

            migrationBuilder.AddForeignKey(
                name: "fk_rutas_colegios_id_colegio",
                table: "rutas",
                column: "id_colegio",
                principalTable: "colegios",
                principalColumn: "id_colegio",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
