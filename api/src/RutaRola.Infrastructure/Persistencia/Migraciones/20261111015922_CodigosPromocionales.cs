using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RutaRola.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class CodigosPromocionales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "codigo_promocional",
                table: "pagos_suscripcion",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "descuento_centavos",
                table: "pagos_suscripcion",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "id_codigo_promocional",
                table: "pagos_suscripcion",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "codigos_promocionales",
                columns: table => new
                {
                    id_codigo_promocional = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    tipo_descuento = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    valor = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    fecha_inicio = table.Column<DateOnly>(type: "date", nullable: false),
                    fecha_fin = table.Column<DateOnly>(type: "date", nullable: false),
                    alcance = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    planes = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    periodo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    usos_maximos = table.Column<int>(type: "integer", nullable: true),
                    usos_por_empresa = table.Column<int>(type: "integer", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_actualizacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_codigos_promocionales", x => x.id_codigo_promocional);
                });

            migrationBuilder.CreateTable(
                name: "codigos_promocionales_empresas",
                columns: table => new
                {
                    id_codigo_promocional = table.Column<Guid>(type: "uuid", nullable: false),
                    id_empresa = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_codigos_promocionales_empresas", x => new { x.id_codigo_promocional, x.id_empresa });
                    table.ForeignKey(
                        name: "fk_codigos_promocionales_empresas_codigos_promocionales_id_cod",
                        column: x => x.id_codigo_promocional,
                        principalTable: "codigos_promocionales",
                        principalColumn: "id_codigo_promocional",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_codigos_promocionales_empresas_empresas_id_empresa",
                        column: x => x.id_empresa,
                        principalTable: "empresas",
                        principalColumn: "id_empresa",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "usos_codigo_promocional",
                columns: table => new
                {
                    id_uso_codigo_promocional = table.Column<Guid>(type: "uuid", nullable: false),
                    id_codigo_promocional = table.Column<Guid>(type: "uuid", nullable: false),
                    id_pago_suscripcion = table.Column<Guid>(type: "uuid", nullable: true),
                    plan = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    periodo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    descuento_centavos = table.Column<long>(type: "bigint", nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_actualizacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    id_empresa = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usos_codigo_promocional", x => x.id_uso_codigo_promocional);
                    table.ForeignKey(
                        name: "fk_usos_codigo_promocional_codigos_promocionales_id_codigo_pro",
                        column: x => x.id_codigo_promocional,
                        principalTable: "codigos_promocionales",
                        principalColumn: "id_codigo_promocional",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_usos_codigo_promocional_empresas_id_empresa",
                        column: x => x.id_empresa,
                        principalTable: "empresas",
                        principalColumn: "id_empresa",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_usos_codigo_promocional_pagos_suscripcion_id_pago_suscripci",
                        column: x => x.id_pago_suscripcion,
                        principalTable: "pagos_suscripcion",
                        principalColumn: "id_pago_suscripcion",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pagos_suscripcion_id_codigo_promocional",
                table: "pagos_suscripcion",
                column: "id_codigo_promocional");

            migrationBuilder.CreateIndex(
                name: "ix_codigos_promocionales_codigo",
                table: "codigos_promocionales",
                column: "codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_codigos_promocionales_empresas_id_empresa",
                table: "codigos_promocionales_empresas",
                column: "id_empresa");

            migrationBuilder.CreateIndex(
                name: "ix_usos_codigo_promocional_id_codigo_promocional_id_empresa",
                table: "usos_codigo_promocional",
                columns: new[] { "id_codigo_promocional", "id_empresa" });

            migrationBuilder.CreateIndex(
                name: "ix_usos_codigo_promocional_id_empresa",
                table: "usos_codigo_promocional",
                column: "id_empresa");

            migrationBuilder.CreateIndex(
                name: "ix_usos_codigo_promocional_id_pago_suscripcion",
                table: "usos_codigo_promocional",
                column: "id_pago_suscripcion");

            migrationBuilder.AddForeignKey(
                name: "fk_pagos_suscripcion_codigos_promocionales_id_codigo_promocion",
                table: "pagos_suscripcion",
                column: "id_codigo_promocional",
                principalTable: "codigos_promocionales",
                principalColumn: "id_codigo_promocional",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_pagos_suscripcion_codigos_promocionales_id_codigo_promocion",
                table: "pagos_suscripcion");

            migrationBuilder.DropTable(
                name: "codigos_promocionales_empresas");

            migrationBuilder.DropTable(
                name: "usos_codigo_promocional");

            migrationBuilder.DropTable(
                name: "codigos_promocionales");

            migrationBuilder.DropIndex(
                name: "ix_pagos_suscripcion_id_codigo_promocional",
                table: "pagos_suscripcion");

            migrationBuilder.DropColumn(
                name: "codigo_promocional",
                table: "pagos_suscripcion");

            migrationBuilder.DropColumn(
                name: "descuento_centavos",
                table: "pagos_suscripcion");

            migrationBuilder.DropColumn(
                name: "id_codigo_promocional",
                table: "pagos_suscripcion");
        }
    }
}
