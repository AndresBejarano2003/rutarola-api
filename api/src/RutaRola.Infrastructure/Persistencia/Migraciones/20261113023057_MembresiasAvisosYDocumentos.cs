using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RutaRola.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class MembresiasAvisosYDocumentos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "foto",
                table: "vehiculos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "dias_prueba",
                table: "suscripciones",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "fecha_ultimo_pago",
                table: "suscripciones",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "fecha_vencimiento",
                table: "suscripciones",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "pendiente_decision",
                table: "suscripciones",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "plan_vencido",
                table: "suscripciones",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tema",
                table: "empresas",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "OSCURO");

            migrationBuilder.CreateTable(
                name: "avisos_ausencia",
                columns: table => new
                {
                    id_aviso_ausencia = table.Column<Guid>(type: "uuid", nullable: false),
                    id_estudiante = table.Column<Guid>(type: "uuid", nullable: false),
                    id_acudiente = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    trayecto = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    motivo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    nota = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    id_confirmado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    nombre_confirmado_por = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    fecha_confirmacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_actualizacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    id_empresa = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_avisos_ausencia", x => x.id_aviso_ausencia);
                    table.ForeignKey(
                        name: "fk_avisos_ausencia_acudientes_id_acudiente",
                        column: x => x.id_acudiente,
                        principalTable: "acudientes",
                        principalColumn: "id_acudiente",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_avisos_ausencia_empresas_id_empresa",
                        column: x => x.id_empresa,
                        principalTable: "empresas",
                        principalColumn: "id_empresa",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_avisos_ausencia_estudiantes_id_estudiante",
                        column: x => x.id_estudiante,
                        principalTable: "estudiantes",
                        principalColumn: "id_estudiante",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "formatos_documentos",
                columns: table => new
                {
                    id_formato_documentos = table.Column<Guid>(type: "uuid", nullable: false),
                    logo = table.Column<string>(type: "text", nullable: true),
                    nombre_comercial = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    eslogan = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    nit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    direccion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ciudad = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    telefono = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    correo = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    sitio_web = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    color_principal = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    pie_pagina = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    nota_factura = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    nota_recibo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    nota_cotizacion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_actualizacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    id_empresa = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_formatos_documentos", x => x.id_formato_documentos);
                    table.ForeignKey(
                        name: "fk_formatos_documentos_empresas_id_empresa",
                        column: x => x.id_empresa,
                        principalTable: "empresas",
                        principalColumn: "id_empresa",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "historial_documentos_vehiculo",
                columns: table => new
                {
                    id_historial_documento = table.Column<Guid>(type: "uuid", nullable: false),
                    id_vehiculo = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_documento = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    numero_documento = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    entidad = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    fecha_expedicion = table.Column<DateOnly>(type: "date", nullable: true),
                    fecha_vencimiento = table.Column<DateOnly>(type: "date", nullable: false),
                    observaciones = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    id_archivo = table.Column<Guid>(type: "uuid", nullable: true),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_actualizacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    id_empresa = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_historial_documentos_vehiculo", x => x.id_historial_documento);
                    table.ForeignKey(
                        name: "fk_historial_documentos_vehiculo_archivos_id_archivo",
                        column: x => x.id_archivo,
                        principalTable: "archivos",
                        principalColumn: "id_archivo",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_historial_documentos_vehiculo_empresas_id_empresa",
                        column: x => x.id_empresa,
                        principalTable: "empresas",
                        principalColumn: "id_empresa",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_historial_documentos_vehiculo_vehiculos_id_vehiculo",
                        column: x => x.id_vehiculo,
                        principalTable: "vehiculos",
                        principalColumn: "id_vehiculo",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "parametros_plataforma",
                columns: table => new
                {
                    id_parametro_plataforma = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    valor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_actualizacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parametros_plataforma", x => x.id_parametro_plataforma);
                });

            migrationBuilder.CreateIndex(
                name: "ix_avisos_ausencia_id_acudiente",
                table: "avisos_ausencia",
                column: "id_acudiente");

            migrationBuilder.CreateIndex(
                name: "ix_avisos_ausencia_id_empresa_fecha",
                table: "avisos_ausencia",
                columns: new[] { "id_empresa", "fecha" });

            migrationBuilder.CreateIndex(
                name: "ix_avisos_ausencia_id_estudiante_fecha",
                table: "avisos_ausencia",
                columns: new[] { "id_estudiante", "fecha" });

            migrationBuilder.CreateIndex(
                name: "ix_formatos_documentos_id_empresa",
                table: "formatos_documentos",
                column: "id_empresa",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_historial_documentos_vehiculo_id_archivo",
                table: "historial_documentos_vehiculo",
                column: "id_archivo");

            migrationBuilder.CreateIndex(
                name: "ix_historial_documentos_vehiculo_id_empresa",
                table: "historial_documentos_vehiculo",
                column: "id_empresa");

            migrationBuilder.CreateIndex(
                name: "ix_historial_documentos_vehiculo_id_vehiculo_tipo_documento_fe",
                table: "historial_documentos_vehiculo",
                columns: new[] { "id_vehiculo", "tipo_documento", "fecha_vencimiento" });

            migrationBuilder.CreateIndex(
                name: "ix_parametros_plataforma_clave",
                table: "parametros_plataforma",
                column: "clave",
                unique: true);

            // El historial arranca con el vencimiento vigente de cada documento de los vehículos existentes.
            migrationBuilder.Sql("""
                INSERT INTO historial_documentos_vehiculo (id_historial_documento, id_vehiculo, tipo_documento, numero_documento, fecha_vencimiento, fecha_creacion, id_empresa)
                SELECT gen_random_uuid(), d.id_vehiculo, d.tipo_documento, d.numero_documento, d.fecha_vencimiento, now(), v.id_empresa
                FROM documentos_vehiculo d JOIN vehiculos v ON v.id_vehiculo = d.id_vehiculo;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "avisos_ausencia");

            migrationBuilder.DropTable(
                name: "formatos_documentos");

            migrationBuilder.DropTable(
                name: "historial_documentos_vehiculo");

            migrationBuilder.DropTable(
                name: "parametros_plataforma");

            migrationBuilder.DropColumn(
                name: "foto",
                table: "vehiculos");

            migrationBuilder.DropColumn(
                name: "dias_prueba",
                table: "suscripciones");

            migrationBuilder.DropColumn(
                name: "fecha_ultimo_pago",
                table: "suscripciones");

            migrationBuilder.DropColumn(
                name: "fecha_vencimiento",
                table: "suscripciones");

            migrationBuilder.DropColumn(
                name: "pendiente_decision",
                table: "suscripciones");

            migrationBuilder.DropColumn(
                name: "plan_vencido",
                table: "suscripciones");

            migrationBuilder.DropColumn(
                name: "tema",
                table: "empresas");
        }
    }
}
