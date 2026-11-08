using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RutaRola.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class SoporteTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tickets_soporte",
                columns: table => new
                {
                    id_ticket = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tipo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    modulo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    prioridad = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    asunto = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    id_solicitante = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre_solicitante = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    correo_solicitante = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    id_responsable = table.Column<Guid>(type: "uuid", nullable: true),
                    nombre_responsable = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    fecha_objetivo = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_primera_respuesta = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    fecha_resolucion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    fecha_cierre = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    fecha_ultima_actividad = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    calificacion = table.Column<int>(type: "integer", nullable: true),
                    comentario_calificacion = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_actualizacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    id_empresa = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tickets_soporte", x => x.id_ticket);
                    table.ForeignKey(
                        name: "fk_tickets_soporte_empresas_id_empresa",
                        column: x => x.id_empresa,
                        principalTable: "empresas",
                        principalColumn: "id_empresa",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tickets_soporte_usuarios_id_responsable",
                        column: x => x.id_responsable,
                        principalTable: "usuarios",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tickets_soporte_usuarios_id_solicitante",
                        column: x => x.id_solicitante,
                        principalTable: "usuarios",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "eventos_ticket",
                columns: table => new
                {
                    id_evento_ticket = table.Column<Guid>(type: "uuid", nullable: false),
                    id_ticket = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    estado_anterior = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    estado_nuevo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    texto = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    interno = table.Column<bool>(type: "boolean", nullable: false),
                    de_la_plataforma = table.Column<bool>(type: "boolean", nullable: false),
                    id_usuario = table.Column<Guid>(type: "uuid", nullable: true),
                    nombre_usuario = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eventos_ticket", x => x.id_evento_ticket);
                    table.ForeignKey(
                        name: "fk_eventos_ticket_tickets_soporte_id_ticket",
                        column: x => x.id_ticket,
                        principalTable: "tickets_soporte",
                        principalColumn: "id_ticket",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reuniones_ticket",
                columns: table => new
                {
                    id_reunion = table.Column<Guid>(type: "uuid", nullable: false),
                    id_ticket = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    duracion_minutos = table.Column<int>(type: "integer", nullable: false),
                    modalidad = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    enlace = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    motivo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    notas = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    nombre_organizador = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reuniones_ticket", x => x.id_reunion);
                    table.ForeignKey(
                        name: "fk_reuniones_ticket_tickets_soporte_id_ticket",
                        column: x => x.id_ticket,
                        principalTable: "tickets_soporte",
                        principalColumn: "id_ticket",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "adjuntos_ticket",
                columns: table => new
                {
                    id_adjunto = table.Column<Guid>(type: "uuid", nullable: false),
                    id_ticket = table.Column<Guid>(type: "uuid", nullable: false),
                    id_evento = table.Column<Guid>(type: "uuid", nullable: true),
                    nombre = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    tipo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    tamano = table.Column<long>(type: "bigint", nullable: false),
                    contenido = table.Column<byte[]>(type: "bytea", nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_adjuntos_ticket", x => x.id_adjunto);
                    table.ForeignKey(
                        name: "fk_adjuntos_ticket_eventos_ticket_id_evento",
                        column: x => x.id_evento,
                        principalTable: "eventos_ticket",
                        principalColumn: "id_evento_ticket",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_adjuntos_ticket_tickets_soporte_id_ticket",
                        column: x => x.id_ticket,
                        principalTable: "tickets_soporte",
                        principalColumn: "id_ticket",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_adjuntos_ticket_id_evento",
                table: "adjuntos_ticket",
                column: "id_evento");

            migrationBuilder.CreateIndex(
                name: "ix_adjuntos_ticket_id_ticket",
                table: "adjuntos_ticket",
                column: "id_ticket");

            migrationBuilder.CreateIndex(
                name: "ix_eventos_ticket_id_ticket_fecha",
                table: "eventos_ticket",
                columns: new[] { "id_ticket", "fecha" });

            migrationBuilder.CreateIndex(
                name: "ix_reuniones_ticket_estado_fecha",
                table: "reuniones_ticket",
                columns: new[] { "estado", "fecha" });

            migrationBuilder.CreateIndex(
                name: "ix_reuniones_ticket_id_ticket",
                table: "reuniones_ticket",
                column: "id_ticket");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_soporte_estado_fecha_ultima_actividad",
                table: "tickets_soporte",
                columns: new[] { "estado", "fecha_ultima_actividad" });

            migrationBuilder.CreateIndex(
                name: "ix_tickets_soporte_id_empresa_fecha_creacion",
                table: "tickets_soporte",
                columns: new[] { "id_empresa", "fecha_creacion" });

            migrationBuilder.CreateIndex(
                name: "ix_tickets_soporte_id_responsable",
                table: "tickets_soporte",
                column: "id_responsable");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_soporte_id_solicitante",
                table: "tickets_soporte",
                column: "id_solicitante");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_soporte_numero",
                table: "tickets_soporte",
                column: "numero",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "adjuntos_ticket");

            migrationBuilder.DropTable(
                name: "reuniones_ticket");

            migrationBuilder.DropTable(
                name: "eventos_ticket");

            migrationBuilder.DropTable(
                name: "tickets_soporte");
        }
    }
}
