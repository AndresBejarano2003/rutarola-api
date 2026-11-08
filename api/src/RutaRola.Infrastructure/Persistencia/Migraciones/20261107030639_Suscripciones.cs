using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RutaRola.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Suscripciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pagos_suscripcion",
                columns: table => new
                {
                    id_pago_suscripcion = table.Column<Guid>(type: "uuid", nullable: false),
                    referencia = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    plan = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    periodo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    valor_centavos = table.Column<long>(type: "bigint", nullable: false),
                    estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    id_transaccion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    metodo_pago = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ambiente = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    id_usuario = table.Column<Guid>(type: "uuid", nullable: true),
                    correo_pagador = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    fecha_aplicacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_actualizacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    id_empresa = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pagos_suscripcion", x => x.id_pago_suscripcion);
                    table.ForeignKey(
                        name: "fk_pagos_suscripcion_empresas_id_empresa",
                        column: x => x.id_empresa,
                        principalTable: "empresas",
                        principalColumn: "id_empresa",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "suscripciones",
                columns: table => new
                {
                    id_suscripcion = table.Column<Guid>(type: "uuid", nullable: false),
                    plan = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    periodo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    fecha_inicio = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_fin = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    prueba_usada = table.Column<bool>(type: "boolean", nullable: false),
                    nota = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_actualizacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    id_empresa = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_suscripciones", x => x.id_suscripcion);
                    table.ForeignKey(
                        name: "fk_suscripciones_empresas_id_empresa",
                        column: x => x.id_empresa,
                        principalTable: "empresas",
                        principalColumn: "id_empresa",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pagos_suscripcion_id_empresa_fecha_creacion",
                table: "pagos_suscripcion",
                columns: new[] { "id_empresa", "fecha_creacion" });

            migrationBuilder.CreateIndex(
                name: "ix_pagos_suscripcion_referencia",
                table: "pagos_suscripcion",
                column: "referencia",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_suscripciones_id_empresa",
                table: "suscripciones",
                column: "id_empresa",
                unique: true);

            // Las empresas que ya existían arrancan con la prueba gratuita (3 meses de Profesional) desde hoy.
            migrationBuilder.Sql("""
                INSERT INTO suscripciones (id_suscripcion, plan, estado, periodo, fecha_inicio, fecha_fin, prueba_usada, nota, fecha_creacion, id_empresa)
                SELECT gen_random_uuid(), 'PROFESIONAL', 'PRUEBA', NULL, now(), now() + interval '3 months', true, NULL, now(), e.id_empresa
                FROM empresas e;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pagos_suscripcion");

            migrationBuilder.DropTable(
                name: "suscripciones");
        }
    }
}
