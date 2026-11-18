using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RutaRola.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class CursosPorJornadaYAsignacionesPorHorario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_colegios_cursos_id_colegio_grado",
                table: "colegios_cursos");

            migrationBuilder.DropIndex(
                name: "ix_asignaciones_ruta_activa",
                table: "asignaciones_ruta");

            migrationBuilder.DropIndex(
                name: "ix_asignaciones_ruta_id_ruta",
                table: "asignaciones_ruta");

            migrationBuilder.AddColumn<string>(
                name: "jornada",
                table: "colegios_cursos",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Única");

            migrationBuilder.AddColumn<TimeOnly>(
                name: "hora_fin",
                table: "asignaciones_ruta",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "hora_inicio",
                table: "asignaciones_ruta",
                type: "time without time zone",
                nullable: true);

            // Datos existentes: cada curso toma la jornada más común de los estudiantes de ese colegio y grado, y se
            // agrega una copia (mismo horario y estado) por cada otra jornada que tengan, para que ningún estudiante
            // quede sin curso. Los cursos sin estudiantes quedan en Única.
            migrationBuilder.Sql("""
                UPDATE colegios_cursos c SET jornada = j.jornada
                FROM (
                    SELECT DISTINCT ON (e.id_colegio, e.grado) e.id_colegio, e.grado, e.jornada
                    FROM estudiantes e
                    WHERE e.jornada IN ('Mañana', 'Tarde', 'Única')
                    GROUP BY e.id_colegio, e.grado, e.jornada
                    ORDER BY e.id_colegio, e.grado, count(*) DESC, e.jornada
                ) j
                WHERE c.id_colegio = j.id_colegio AND c.grado = j.grado;

                INSERT INTO colegios_cursos (id_curso, id_colegio, grado, jornada, hora_entrada, hora_salida, activo)
                SELECT gen_random_uuid(), c.id_colegio, c.grado, j.jornada, c.hora_entrada, c.hora_salida, c.activo
                FROM colegios_cursos c
                JOIN (
                    SELECT DISTINCT id_colegio, grado, jornada FROM estudiantes WHERE jornada IN ('Mañana', 'Tarde', 'Única')
                ) j ON j.id_colegio = c.id_colegio AND j.grado = c.grado AND j.jornada <> c.jornada;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_colegios_cursos_id_colegio_grado_jornada",
                table: "colegios_cursos",
                columns: new[] { "id_colegio", "grado", "jornada" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_asignaciones_ruta_id_ruta_fecha_inicio",
                table: "asignaciones_ruta",
                columns: new[] { "id_ruta", "fecha_inicio" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_colegios_cursos_id_colegio_grado_jornada",
                table: "colegios_cursos");

            migrationBuilder.DropIndex(
                name: "ix_asignaciones_ruta_id_ruta_fecha_inicio",
                table: "asignaciones_ruta");

            migrationBuilder.DropColumn(
                name: "jornada",
                table: "colegios_cursos");

            migrationBuilder.DropColumn(
                name: "hora_fin",
                table: "asignaciones_ruta");

            migrationBuilder.DropColumn(
                name: "hora_inicio",
                table: "asignaciones_ruta");

            migrationBuilder.CreateIndex(
                name: "ix_colegios_cursos_id_colegio_grado",
                table: "colegios_cursos",
                columns: new[] { "id_colegio", "grado" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_asignaciones_ruta_activa",
                table: "asignaciones_ruta",
                column: "id_ruta",
                unique: true,
                filter: "activo");

            migrationBuilder.CreateIndex(
                name: "ix_asignaciones_ruta_id_ruta",
                table: "asignaciones_ruta",
                column: "id_ruta");
        }
    }
}
