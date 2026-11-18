using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RutaRola.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class NovedadesYGastosVehiculo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_gastos_id_vehiculo",
                table: "gastos");

            // La categoría libre anterior pasa a la descripción (los gastos ahora tienen tipo) y se acota a 300.
            migrationBuilder.Sql("""
                UPDATE gastos SET descripcion = left(trim(both ' —' from coalesce(categoria, '') || ' — ' || coalesce(descripcion, '')), 300)
                WHERE coalesce(categoria, '') <> '' OR length(coalesce(descripcion, '')) > 300;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "descripcion",
                table: "gastos",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "cantidad",
                table: "gastos",
                type: "numeric(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "id_archivo",
                table: "gastos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "kilometraje",
                table: "gastos",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "proveedor",
                table: "gastos",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "referencia",
                table: "gastos",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tipo",
                table: "gastos",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "OTRO");

            migrationBuilder.DropColumn(
                name: "categoria",
                table: "gastos");

            migrationBuilder.CreateTable(
                name: "novedades_vehiculo",
                columns: table => new
                {
                    id_novedad = table.Column<Guid>(type: "uuid", nullable: false),
                    id_vehiculo = table.Column<Guid>(type: "uuid", nullable: false),
                    id_empleado = table.Column<Guid>(type: "uuid", nullable: true),
                    nombre_reporta = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    tipo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    gravedad = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    kilometraje = table.Column<int>(type: "integer", nullable: true),
                    estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    respuesta = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    nombre_atiende = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    fecha_atencion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    id_archivo = table.Column<Guid>(type: "uuid", nullable: true),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_actualizacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    id_empresa = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_novedades_vehiculo", x => x.id_novedad);
                    table.ForeignKey(
                        name: "fk_novedades_vehiculo_archivos_id_archivo",
                        column: x => x.id_archivo,
                        principalTable: "archivos",
                        principalColumn: "id_archivo",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_novedades_vehiculo_empleados_id_empleado",
                        column: x => x.id_empleado,
                        principalTable: "empleados",
                        principalColumn: "id_empleado",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_novedades_vehiculo_empresas_id_empresa",
                        column: x => x.id_empresa,
                        principalTable: "empresas",
                        principalColumn: "id_empresa",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_novedades_vehiculo_vehiculos_id_vehiculo",
                        column: x => x.id_vehiculo,
                        principalTable: "vehiculos",
                        principalColumn: "id_vehiculo",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_gastos_id_archivo",
                table: "gastos",
                column: "id_archivo");

            migrationBuilder.CreateIndex(
                name: "ix_gastos_id_vehiculo_fecha_gasto",
                table: "gastos",
                columns: new[] { "id_vehiculo", "fecha_gasto" });

            migrationBuilder.CreateIndex(
                name: "ix_novedades_vehiculo_id_archivo",
                table: "novedades_vehiculo",
                column: "id_archivo");

            migrationBuilder.CreateIndex(
                name: "ix_novedades_vehiculo_id_empleado",
                table: "novedades_vehiculo",
                column: "id_empleado");

            migrationBuilder.CreateIndex(
                name: "ix_novedades_vehiculo_id_empresa_estado",
                table: "novedades_vehiculo",
                columns: new[] { "id_empresa", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_novedades_vehiculo_id_vehiculo_estado",
                table: "novedades_vehiculo",
                columns: new[] { "id_vehiculo", "estado" });

            migrationBuilder.AddForeignKey(
                name: "fk_gastos_archivos_id_archivo",
                table: "gastos",
                column: "id_archivo",
                principalTable: "archivos",
                principalColumn: "id_archivo",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_gastos_archivos_id_archivo",
                table: "gastos");

            migrationBuilder.DropTable(
                name: "novedades_vehiculo");

            migrationBuilder.DropIndex(
                name: "ix_gastos_id_archivo",
                table: "gastos");

            migrationBuilder.DropIndex(
                name: "ix_gastos_id_vehiculo_fecha_gasto",
                table: "gastos");

            migrationBuilder.DropColumn(
                name: "cantidad",
                table: "gastos");

            migrationBuilder.DropColumn(
                name: "id_archivo",
                table: "gastos");

            migrationBuilder.DropColumn(
                name: "kilometraje",
                table: "gastos");

            migrationBuilder.DropColumn(
                name: "proveedor",
                table: "gastos");

            migrationBuilder.DropColumn(
                name: "referencia",
                table: "gastos");

            migrationBuilder.DropColumn(
                name: "tipo",
                table: "gastos");

            migrationBuilder.AlterColumn<string>(
                name: "descripcion",
                table: "gastos",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "categoria",
                table: "gastos",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_gastos_id_vehiculo",
                table: "gastos",
                column: "id_vehiculo");
        }
    }
}
