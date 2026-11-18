using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RutaRola.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class FinanzasAbonosRetirosYCotizaciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_pagos_vigente_por_periodo",
                table: "pagos");

            migrationBuilder.RenameColumn(
                name: "valor_por_estudiante",
                table: "cotizaciones",
                newName: "valor_mensual");

            migrationBuilder.AddColumn<Guid>(
                name: "id_cotizacion",
                table: "facturas",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_retiro",
                table: "facturas",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "periodo_retiro",
                table: "facturas",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "id_colegio",
                table: "cotizaciones_estudiantes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "nombre_colegio",
                table: "cotizaciones_estudiantes",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "valor",
                table: "cotizaciones_estudiantes",
                type: "numeric(14,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "fecha_facturacion",
                table: "cotizaciones",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "periodo_fin",
                table: "cotizaciones",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<DateOnly>(
                name: "periodo_inicio",
                table: "cotizaciones",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.CreateTable(
                name: "pagos_detalle",
                columns: table => new
                {
                    id_pago_detalle = table.Column<Guid>(type: "uuid", nullable: false),
                    id_pago = table.Column<Guid>(type: "uuid", nullable: false),
                    periodo = table.Column<DateOnly>(type: "date", nullable: false),
                    valor = table.Column<decimal>(type: "numeric(14,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pagos_detalle", x => x.id_pago_detalle);
                    table.ForeignKey(
                        name: "fk_pagos_detalle_pagos_id_pago",
                        column: x => x.id_pago,
                        principalTable: "pagos",
                        principalColumn: "id_pago",
                        onDelete: ReferentialAction.Cascade);
                });

            // Datos existentes: cada recibo pagaba un mes completo → una línea de detalle con su valor; las cotizaciones
            // tenían un valor por estudiante (ahora en cada línea, valor_mensual = suma) y servían el mes de su fecha.
            migrationBuilder.Sql("""
                INSERT INTO pagos_detalle (id_pago_detalle, id_pago, periodo, valor)
                SELECT gen_random_uuid(), p.id_pago, p.periodo, p.valor FROM pagos p WHERE p.periodo IS NOT NULL;

                UPDATE cotizaciones_estudiantes ce SET valor = c.valor_mensual
                FROM cotizaciones c WHERE c.id_cotizacion = ce.id_cotizacion;

                UPDATE cotizaciones_estudiantes ce SET id_colegio = e.id_colegio, nombre_colegio = co.razon_social
                FROM estudiantes e JOIN colegios co ON co.id_colegio = e.id_colegio
                WHERE ce.id_estudiante = e.id_estudiante;

                UPDATE cotizaciones c SET
                    periodo_inicio = date_trunc('month', c.fecha)::date,
                    periodo_fin = date_trunc('month', c.fecha)::date,
                    valor_mensual = COALESCE((SELECT SUM(ce.valor) FROM cotizaciones_estudiantes ce WHERE ce.id_cotizacion = c.id_cotizacion), 0);
                """);

            migrationBuilder.CreateIndex(
                name: "ix_facturas_id_cotizacion",
                table: "facturas",
                column: "id_cotizacion");

            migrationBuilder.CreateIndex(
                name: "ix_cotizaciones_estudiantes_id_colegio",
                table: "cotizaciones_estudiantes",
                column: "id_colegio");

            migrationBuilder.CreateIndex(
                name: "ix_pagos_detalle_id_pago_periodo",
                table: "pagos_detalle",
                columns: new[] { "id_pago", "periodo" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_cotizaciones_estudiantes_colegios_id_colegio",
                table: "cotizaciones_estudiantes",
                column: "id_colegio",
                principalTable: "colegios",
                principalColumn: "id_colegio",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_facturas_cotizaciones_id_cotizacion",
                table: "facturas",
                column: "id_cotizacion",
                principalTable: "cotizaciones",
                principalColumn: "id_cotizacion",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_cotizaciones_estudiantes_colegios_id_colegio",
                table: "cotizaciones_estudiantes");

            migrationBuilder.DropForeignKey(
                name: "fk_facturas_cotizaciones_id_cotizacion",
                table: "facturas");

            migrationBuilder.DropTable(
                name: "pagos_detalle");

            migrationBuilder.DropIndex(
                name: "ix_facturas_id_cotizacion",
                table: "facturas");

            migrationBuilder.DropIndex(
                name: "ix_cotizaciones_estudiantes_id_colegio",
                table: "cotizaciones_estudiantes");

            migrationBuilder.DropColumn(
                name: "id_cotizacion",
                table: "facturas");

            migrationBuilder.DropColumn(
                name: "motivo_retiro",
                table: "facturas");

            migrationBuilder.DropColumn(
                name: "periodo_retiro",
                table: "facturas");

            migrationBuilder.DropColumn(
                name: "id_colegio",
                table: "cotizaciones_estudiantes");

            migrationBuilder.DropColumn(
                name: "nombre_colegio",
                table: "cotizaciones_estudiantes");

            migrationBuilder.DropColumn(
                name: "valor",
                table: "cotizaciones_estudiantes");

            migrationBuilder.DropColumn(
                name: "fecha_facturacion",
                table: "cotizaciones");

            migrationBuilder.DropColumn(
                name: "periodo_fin",
                table: "cotizaciones");

            migrationBuilder.DropColumn(
                name: "periodo_inicio",
                table: "cotizaciones");

            migrationBuilder.RenameColumn(
                name: "valor_mensual",
                table: "cotizaciones",
                newName: "valor_por_estudiante");

            migrationBuilder.CreateIndex(
                name: "ix_pagos_vigente_por_periodo",
                table: "pagos",
                columns: new[] { "id_factura", "periodo" },
                unique: true,
                filter: "estado = 'VIGENTE' AND id_factura IS NOT NULL");
        }
    }
}
