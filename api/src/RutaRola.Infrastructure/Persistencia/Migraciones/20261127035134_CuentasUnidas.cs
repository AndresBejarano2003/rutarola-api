using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RutaRola.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class CuentasUnidas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cuentas_unidas",
                columns: table => new
                {
                    id_cuenta_unida = table.Column<Guid>(type: "uuid", nullable: false),
                    id_grupo = table.Column<Guid>(type: "uuid", nullable: false),
                    id_usuario = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cuentas_unidas", x => x.id_cuenta_unida);
                    table.ForeignKey(
                        name: "fk_cuentas_unidas_usuarios_id_usuario",
                        column: x => x.id_usuario,
                        principalTable: "usuarios",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cuentas_unidas_id_grupo",
                table: "cuentas_unidas",
                column: "id_grupo");

            migrationBuilder.CreateIndex(
                name: "ix_cuentas_unidas_id_usuario",
                table: "cuentas_unidas",
                column: "id_usuario",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cuentas_unidas");
        }
    }
}
