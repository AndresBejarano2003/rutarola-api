using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RutaRola.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "empresas",
                columns: table => new
                {
                    id_empresa = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    nit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    direccion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ciudad = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    telefono = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    email = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    logo_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_actualizacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_empresas", x => x.id_empresa);
                });

            migrationBuilder.CreateTable(
                name: "permisos",
                columns: table => new
                {
                    id_permiso = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_actualizacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permisos", x => x.id_permiso);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    id_rol = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_actualizacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id_rol);
                });

            migrationBuilder.CreateTable(
                name: "colegios",
                columns: table => new
                {
                    id_colegio = table.Column<Guid>(type: "uuid", nullable: false),
                    nit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    razon_social = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    direccion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    telefono = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    persona_contacto = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    latitud = table.Column<decimal>(type: "numeric(9,6)", nullable: true),
                    longitud = table.Column<decimal>(type: "numeric(9,6)", nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_actualizacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    id_empresa = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_colegios", x => x.id_colegio);
                    table.ForeignKey(
                        name: "fk_colegios_empresas_id_empresa",
                        column: x => x.id_empresa,
                        principalTable: "empresas",
                        principalColumn: "id_empresa",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "configuracion_sistema",
                columns: table => new
                {
                    id_configuracion = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    valor = table.Column<string>(type: "text", nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_actualizacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    id_empresa = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_configuracion_sistema", x => x.id_configuracion);
                    table.ForeignKey(
                        name: "fk_configuracion_sistema_empresas_id_empresa",
                        column: x => x.id_empresa,
                        principalTable: "empresas",
                        principalColumn: "id_empresa",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vehiculos",
                columns: table => new
                {
                    id_vehiculo = table.Column<Guid>(type: "uuid", nullable: false),
                    placa = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    numero_interno = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tipo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    marca = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    linea = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    anio = table.Column<int>(type: "integer", nullable: false),
                    color = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    capacidad = table.Column<int>(type: "integer", nullable: false),
                    observaciones = table.Column<string>(type: "text", nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_actualizacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    id_empresa = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vehiculos", x => x.id_vehiculo);
                    table.ForeignKey(
                        name: "fk_vehiculos_empresas_id_empresa",
                        column: x => x.id_empresa,
                        principalTable: "empresas",
                        principalColumn: "id_empresa",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "roles_permisos",
                columns: table => new
                {
                    id_rol_permiso = table.Column<Guid>(type: "uuid", nullable: false),
                    id_rol = table.Column<Guid>(type: "uuid", nullable: false),
                    id_permiso = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles_permisos", x => x.id_rol_permiso);
                    table.ForeignKey(
                        name: "fk_roles_permisos_permisos_id_permiso",
                        column: x => x.id_permiso,
                        principalTable: "permisos",
                        principalColumn: "id_permiso",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_roles_permisos_roles_id_rol",
                        column: x => x.id_rol,
                        principalTable: "roles",
                        principalColumn: "id_rol",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usuarios",
                columns: table => new
                {
                    id_usuario = table.Column<Guid>(type: "uuid", nullable: false),
                    id_empresa = table.Column<Guid>(type: "uuid", nullable: true),
                    id_rol = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    usuario = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    email = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    telefono = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    password_hash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    ultimo_login = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_actualizacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuarios", x => x.id_usuario);
                    table.ForeignKey(
                        name: "fk_usuarios_empresas_id_empresa",
                        column: x => x.id_empresa,
                        principalTable: "empresas",
                        principalColumn: "id_empresa",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_usuarios_roles_id_rol",
                        column: x => x.id_rol,
                        principalTable: "roles",
                        principalColumn: "id_rol",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "colegios_cursos",
                columns: table => new
                {
                    id_curso = table.Column<Guid>(type: "uuid", nullable: false),
                    id_colegio = table.Column<Guid>(type: "uuid", nullable: false),
                    grado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    hora_entrada = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    hora_salida = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_colegios_cursos", x => x.id_curso);
                    table.ForeignKey(
                        name: "fk_colegios_cursos_colegios_id_colegio",
                        column: x => x.id_colegio,
                        principalTable: "colegios",
                        principalColumn: "id_colegio",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "estudiantes",
                columns: table => new
                {
                    id_estudiante = table.Column<Guid>(type: "uuid", nullable: false),
                    id_colegio = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha_nacimiento = table.Column<DateOnly>(type: "date", nullable: false),
                    direccion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    latitud = table.Column<decimal>(type: "numeric(9,6)", nullable: true),
                    longitud = table.Column<decimal>(type: "numeric(9,6)", nullable: true),
                    grado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    jornada = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    observaciones = table.Column<string>(type: "text", nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_actualizacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    id_empresa = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_documento = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    numero_documento = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    primer_nombre = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    segundo_nombre = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    primer_apellido = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    segundo_apellido = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_estudiantes", x => x.id_estudiante);
                    table.ForeignKey(
                        name: "fk_estudiantes_colegios_id_colegio",
                        column: x => x.id_colegio,
                        principalTable: "colegios",
                        principalColumn: "id_colegio",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_estudiantes_empresas_id_empresa",
                        column: x => x.id_empresa,
                        principalTable: "empresas",
                        principalColumn: "id_empresa",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rutas",
                columns: table => new
                {
                    id_ruta = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    id_colegio = table.Column<Guid>(type: "uuid", nullable: false),
                    jornada = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    sector = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    observaciones = table.Column<string>(type: "text", nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_actualizacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    id_empresa = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rutas", x => x.id_ruta);
                    table.ForeignKey(
                        name: "fk_rutas_colegios_id_colegio",
                        column: x => x.id_colegio,
                        principalTable: "colegios",
                        principalColumn: "id_colegio",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rutas_empresas_id_empresa",
                        column: x => x.id_empresa,
                        principalTable: "empresas",
                        principalColumn: "id_empresa",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "acudientes",
                columns: table => new
                {
                    id_acudiente = table.Column<Guid>(type: "uuid", nullable: false),
                    id_usuario = table.Column<Guid>(type: "uuid", nullable: true),
                    telefono = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    telefono_alterno = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    correo = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    direccion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    parentesco = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_actualizacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    id_empresa = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_documento = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    numero_documento = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    primer_nombre = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    segundo_nombre = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    primer_apellido = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    segundo_apellido = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acudientes", x => x.id_acudiente);
                    table.ForeignKey(
                        name: "fk_acudientes_empresas_id_empresa",
                        column: x => x.id_empresa,
                        principalTable: "empresas",
                        principalColumn: "id_empresa",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_acudientes_usuarios_id_usuario",
                        column: x => x.id_usuario,
                        principalTable: "usuarios",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "archivos",
                columns: table => new
                {
                    id_archivo = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    tipo_archivo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    tamano = table.Column<long>(type: "bigint", nullable: false),
                    url_archivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    contenido = table.Column<byte[]>(type: "bytea", nullable: true),
                    subido_por = table.Column<Guid>(type: "uuid", nullable: true),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_actualizacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    id_empresa = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_archivos", x => x.id_archivo);
                    table.ForeignKey(
                        name: "fk_archivos_empresas_id_empresa",
                        column: x => x.id_empresa,
                        principalTable: "empresas",
                        principalColumn: "id_empresa",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_archivos_usuarios_subido_por",
                        column: x => x.subido_por,
                        principalTable: "usuarios",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "auditoria",
                columns: table => new
                {
                    id_auditoria = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    id_empresa = table.Column<Guid>(type: "uuid", nullable: true),
                    id_usuario = table.Column<Guid>(type: "uuid", nullable: true),
                    accion = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    tabla_afectada = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    id_registro = table.Column<Guid>(type: "uuid", nullable: false),
                    valor_anterior = table.Column<string>(type: "jsonb", nullable: true),
                    valor_nuevo = table.Column<string>(type: "jsonb", nullable: true),
                    fecha_accion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    direccion_ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auditoria", x => x.id_auditoria);
                    table.ForeignKey(
                        name: "fk_auditoria_empresas_id_empresa",
                        column: x => x.id_empresa,
                        principalTable: "empresas",
                        principalColumn: "id_empresa",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_auditoria_usuarios_id_usuario",
                        column: x => x.id_usuario,
                        principalTable: "usuarios",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "gastos",
                columns: table => new
                {
                    id_gasto = table.Column<Guid>(type: "uuid", nullable: false),
                    id_vehiculo = table.Column<Guid>(type: "uuid", nullable: true),
                    categoria = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    descripcion = table.Column<string>(type: "text", nullable: true),
                    valor = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    fecha_gasto = table.Column<DateOnly>(type: "date", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_actualizacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    id_empresa = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gastos", x => x.id_gasto);
                    table.ForeignKey(
                        name: "fk_gastos_empresas_id_empresa",
                        column: x => x.id_empresa,
                        principalTable: "empresas",
                        principalColumn: "id_empresa",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_gastos_usuarios_creado_por",
                        column: x => x.creado_por,
                        principalTable: "usuarios",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_gastos_vehiculos_id_vehiculo",
                        column: x => x.id_vehiculo,
                        principalTable: "vehiculos",
                        principalColumn: "id_vehiculo",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notificaciones",
                columns: table => new
                {
                    id_notificacion = table.Column<Guid>(type: "uuid", nullable: false),
                    id_usuario = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    categoria = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    nivel = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    titulo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    mensaje = table.Column<string>(type: "text", nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    leida = table.Column<bool>(type: "boolean", nullable: false),
                    archivada = table.Column<bool>(type: "boolean", nullable: false),
                    eliminada = table.Column<bool>(type: "boolean", nullable: false),
                    enlace = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    texto_enlace = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notificaciones", x => x.id_notificacion);
                    table.ForeignKey(
                        name: "fk_notificaciones_usuarios_id_usuario",
                        column: x => x.id_usuario,
                        principalTable: "usuarios",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sesiones_usuario",
                columns: table => new
                {
                    id_sesion = table.Column<Guid>(type: "uuid", nullable: false),
                    id_usuario = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    direccion_ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    dispositivo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_expiracion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revocado = table.Column<bool>(type: "boolean", nullable: false),
                    fecha_revocacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    id_reemplazada_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sesiones_usuario", x => x.id_sesion);
                    table.ForeignKey(
                        name: "fk_sesiones_usuario_sesiones_usuario_id_reemplazada_por",
                        column: x => x.id_reemplazada_por,
                        principalTable: "sesiones_usuario",
                        principalColumn: "id_sesion",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sesiones_usuario_usuarios_id_usuario",
                        column: x => x.id_usuario,
                        principalTable: "usuarios",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rutas_estudiantes",
                columns: table => new
                {
                    id_ruta_estudiante = table.Column<Guid>(type: "uuid", nullable: false),
                    id_ruta = table.Column<Guid>(type: "uuid", nullable: false),
                    id_estudiante = table.Column<Guid>(type: "uuid", nullable: false),
                    orden_recogida = table.Column<int>(type: "integer", nullable: false),
                    hora_recogida_estimada = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    hora_llegada_estimada = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rutas_estudiantes", x => x.id_ruta_estudiante);
                    table.ForeignKey(
                        name: "fk_rutas_estudiantes_estudiantes_id_estudiante",
                        column: x => x.id_estudiante,
                        principalTable: "estudiantes",
                        principalColumn: "id_estudiante",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rutas_estudiantes_rutas_id_ruta",
                        column: x => x.id_ruta,
                        principalTable: "rutas",
                        principalColumn: "id_ruta",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cotizaciones",
                columns: table => new
                {
                    id_cotizacion = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    id_acudiente = table.Column<Guid>(type: "uuid", nullable: true),
                    nombre_acudiente = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    telefono_acudiente = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    valor_por_estudiante = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    valor_total = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    observaciones = table.Column<string>(type: "text", nullable: true),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_actualizacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    id_empresa = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cotizaciones", x => x.id_cotizacion);
                    table.ForeignKey(
                        name: "fk_cotizaciones_acudientes_id_acudiente",
                        column: x => x.id_acudiente,
                        principalTable: "acudientes",
                        principalColumn: "id_acudiente",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cotizaciones_empresas_id_empresa",
                        column: x => x.id_empresa,
                        principalTable: "empresas",
                        principalColumn: "id_empresa",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "estudiantes_acudientes",
                columns: table => new
                {
                    id_estudiante = table.Column<Guid>(type: "uuid", nullable: false),
                    id_acudiente = table.Column<Guid>(type: "uuid", nullable: false),
                    es_principal = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_estudiantes_acudientes", x => new { x.id_estudiante, x.id_acudiente });
                    table.ForeignKey(
                        name: "fk_estudiantes_acudientes_acudientes_id_acudiente",
                        column: x => x.id_acudiente,
                        principalTable: "acudientes",
                        principalColumn: "id_acudiente",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_estudiantes_acudientes_estudiantes_id_estudiante",
                        column: x => x.id_estudiante,
                        principalTable: "estudiantes",
                        principalColumn: "id_estudiante",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "facturas",
                columns: table => new
                {
                    id_factura = table.Column<Guid>(type: "uuid", nullable: false),
                    numero_factura = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    id_acudiente = table.Column<Guid>(type: "uuid", nullable: false),
                    id_estudiante = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre_acudiente = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    nombre_estudiante = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    valor_mensual = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    periodo_inicio = table.Column<DateOnly>(type: "date", nullable: false),
                    periodo_fin = table.Column<DateOnly>(type: "date", nullable: false),
                    concepto = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    valor_total = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    fecha_emision = table.Column<DateOnly>(type: "date", nullable: false),
                    fecha_vencimiento = table.Column<DateOnly>(type: "date", nullable: true),
                    estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    url_pdf = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    observaciones = table.Column<string>(type: "text", nullable: true),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_actualizacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    id_empresa = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_facturas", x => x.id_factura);
                    table.ForeignKey(
                        name: "fk_facturas_acudientes_id_acudiente",
                        column: x => x.id_acudiente,
                        principalTable: "acudientes",
                        principalColumn: "id_acudiente",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_facturas_empresas_id_empresa",
                        column: x => x.id_empresa,
                        principalTable: "empresas",
                        principalColumn: "id_empresa",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_facturas_estudiantes_id_estudiante",
                        column: x => x.id_estudiante,
                        principalTable: "estudiantes",
                        principalColumn: "id_estudiante",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mensajes_whatsapp",
                columns: table => new
                {
                    id_mensaje = table.Column<Guid>(type: "uuid", nullable: false),
                    id_acudiente = table.Column<Guid>(type: "uuid", nullable: false),
                    id_estudiante = table.Column<Guid>(type: "uuid", nullable: true),
                    telefono = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    mensaje = table.Column<string>(type: "text", nullable: false),
                    estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_envio = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    respuesta = table.Column<string>(type: "text", nullable: true),
                    id_externo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mensajes_whatsapp", x => x.id_mensaje);
                    table.ForeignKey(
                        name: "fk_mensajes_whatsapp_acudientes_id_acudiente",
                        column: x => x.id_acudiente,
                        principalTable: "acudientes",
                        principalColumn: "id_acudiente",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mensajes_whatsapp_estudiantes_id_estudiante",
                        column: x => x.id_estudiante,
                        principalTable: "estudiantes",
                        principalColumn: "id_estudiante",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "documentos_vehiculo",
                columns: table => new
                {
                    id_documento = table.Column<Guid>(type: "uuid", nullable: false),
                    id_vehiculo = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_documento = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    numero_documento = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    fecha_vencimiento = table.Column<DateOnly>(type: "date", nullable: false),
                    id_archivo = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documentos_vehiculo", x => x.id_documento);
                    table.ForeignKey(
                        name: "fk_documentos_vehiculo_archivos_id_archivo",
                        column: x => x.id_archivo,
                        principalTable: "archivos",
                        principalColumn: "id_archivo",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_documentos_vehiculo_vehiculos_id_vehiculo",
                        column: x => x.id_vehiculo,
                        principalTable: "vehiculos",
                        principalColumn: "id_vehiculo",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "empleados",
                columns: table => new
                {
                    id_empleado = table.Column<Guid>(type: "uuid", nullable: false),
                    id_usuario = table.Column<Guid>(type: "uuid", nullable: true),
                    tipo_empleado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    telefono = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    correo = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    direccion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    fecha_ingreso = table.Column<DateOnly>(type: "date", nullable: false),
                    sueldo = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    tipo_contrato = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    id_archivo_contrato = table.Column<Guid>(type: "uuid", nullable: true),
                    numero_licencia = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    categoria_licencia = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    fecha_vencimiento_licencia = table.Column<DateOnly>(type: "date", nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_actualizacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    id_empresa = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_documento = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    numero_documento = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    primer_nombre = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    segundo_nombre = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    primer_apellido = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    segundo_apellido = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_empleados", x => x.id_empleado);
                    table.ForeignKey(
                        name: "fk_empleados_archivos_id_archivo_contrato",
                        column: x => x.id_archivo_contrato,
                        principalTable: "archivos",
                        principalColumn: "id_archivo",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_empleados_empresas_id_empresa",
                        column: x => x.id_empresa,
                        principalTable: "empresas",
                        principalColumn: "id_empresa",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_empleados_usuarios_id_usuario",
                        column: x => x.id_usuario,
                        principalTable: "usuarios",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cotizaciones_estudiantes",
                columns: table => new
                {
                    id_cotizacion_estudiante = table.Column<Guid>(type: "uuid", nullable: false),
                    id_cotizacion = table.Column<Guid>(type: "uuid", nullable: false),
                    id_estudiante = table.Column<Guid>(type: "uuid", nullable: true),
                    nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cotizaciones_estudiantes", x => x.id_cotizacion_estudiante);
                    table.ForeignKey(
                        name: "fk_cotizaciones_estudiantes_cotizaciones_id_cotizacion",
                        column: x => x.id_cotizacion,
                        principalTable: "cotizaciones",
                        principalColumn: "id_cotizacion",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_cotizaciones_estudiantes_estudiantes_id_estudiante",
                        column: x => x.id_estudiante,
                        principalTable: "estudiantes",
                        principalColumn: "id_estudiante",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "facturas_detalle",
                columns: table => new
                {
                    id_detalle = table.Column<Guid>(type: "uuid", nullable: false),
                    id_factura = table.Column<Guid>(type: "uuid", nullable: false),
                    periodo = table.Column<DateOnly>(type: "date", nullable: true),
                    descripcion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    cantidad = table.Column<int>(type: "integer", nullable: false),
                    precio_unitario = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    total = table.Column<decimal>(type: "numeric(14,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_facturas_detalle", x => x.id_detalle);
                    table.ForeignKey(
                        name: "fk_facturas_detalle_facturas_id_factura",
                        column: x => x.id_factura,
                        principalTable: "facturas",
                        principalColumn: "id_factura",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pagos",
                columns: table => new
                {
                    id_pago = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    id_estudiante = table.Column<Guid>(type: "uuid", nullable: false),
                    id_factura = table.Column<Guid>(type: "uuid", nullable: true),
                    periodo = table.Column<DateOnly>(type: "date", nullable: true),
                    valor = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    fecha_pago = table.Column<DateOnly>(type: "date", nullable: false),
                    metodo_pago = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    numero_referencia = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    numero_factura = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    nombre_acudiente = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    nombre_estudiante = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    observaciones = table.Column<string>(type: "text", nullable: true),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_actualizacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    id_empresa = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pagos", x => x.id_pago);
                    table.ForeignKey(
                        name: "fk_pagos_empresas_id_empresa",
                        column: x => x.id_empresa,
                        principalTable: "empresas",
                        principalColumn: "id_empresa",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pagos_estudiantes_id_estudiante",
                        column: x => x.id_estudiante,
                        principalTable: "estudiantes",
                        principalColumn: "id_estudiante",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pagos_facturas_id_factura",
                        column: x => x.id_factura,
                        principalTable: "facturas",
                        principalColumn: "id_factura",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "programaciones_cobro",
                columns: table => new
                {
                    id_programacion = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    id_factura = table.Column<Guid>(type: "uuid", nullable: false),
                    dia_limite_pago = table.Column<int>(type: "integer", nullable: false),
                    dias_previos = table.Column<int>(type: "integer", nullable: false),
                    whatsapp = table.Column<bool>(type: "boolean", nullable: false),
                    email = table.Column<bool>(type: "boolean", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_actualizacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    id_empresa = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_programaciones_cobro", x => x.id_programacion);
                    table.ForeignKey(
                        name: "fk_programaciones_cobro_empresas_id_empresa",
                        column: x => x.id_empresa,
                        principalTable: "empresas",
                        principalColumn: "id_empresa",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_programaciones_cobro_facturas_id_factura",
                        column: x => x.id_factura,
                        principalTable: "facturas",
                        principalColumn: "id_factura",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "asignaciones_ruta",
                columns: table => new
                {
                    id_asignacion = table.Column<Guid>(type: "uuid", nullable: false),
                    id_ruta = table.Column<Guid>(type: "uuid", nullable: false),
                    id_vehiculo = table.Column<Guid>(type: "uuid", nullable: false),
                    id_conductor = table.Column<Guid>(type: "uuid", nullable: false),
                    id_monitora = table.Column<Guid>(type: "uuid", nullable: true),
                    fecha_inicio = table.Column<DateOnly>(type: "date", nullable: false),
                    fecha_fin = table.Column<DateOnly>(type: "date", nullable: false),
                    observaciones = table.Column<string>(type: "text", nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_actualizacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    id_empresa = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asignaciones_ruta", x => x.id_asignacion);
                    table.ForeignKey(
                        name: "fk_asignaciones_ruta_empleados_id_conductor",
                        column: x => x.id_conductor,
                        principalTable: "empleados",
                        principalColumn: "id_empleado",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_asignaciones_ruta_empleados_id_monitora",
                        column: x => x.id_monitora,
                        principalTable: "empleados",
                        principalColumn: "id_empleado",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_asignaciones_ruta_empresas_id_empresa",
                        column: x => x.id_empresa,
                        principalTable: "empresas",
                        principalColumn: "id_empresa",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_asignaciones_ruta_rutas_id_ruta",
                        column: x => x.id_ruta,
                        principalTable: "rutas",
                        principalColumn: "id_ruta",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_asignaciones_ruta_vehiculos_id_vehiculo",
                        column: x => x.id_vehiculo,
                        principalTable: "vehiculos",
                        principalColumn: "id_vehiculo",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "envios_programacion",
                columns: table => new
                {
                    id_envio = table.Column<Guid>(type: "uuid", nullable: false),
                    id_programacion = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    canal = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    destinatario = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    periodo = table.Column<DateOnly>(type: "date", nullable: false),
                    estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    detalle = table.Column<string>(type: "text", nullable: true),
                    id_mensaje_whatsapp = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_envios_programacion", x => x.id_envio);
                    table.ForeignKey(
                        name: "fk_envios_programacion_mensajes_whatsapp_id_mensaje_whatsapp",
                        column: x => x.id_mensaje_whatsapp,
                        principalTable: "mensajes_whatsapp",
                        principalColumn: "id_mensaje",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_envios_programacion_programaciones_cobro_id_programacion",
                        column: x => x.id_programacion,
                        principalTable: "programaciones_cobro",
                        principalColumn: "id_programacion",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "plantillas_mensaje",
                columns: table => new
                {
                    id_plantilla = table.Column<Guid>(type: "uuid", nullable: false),
                    id_programacion = table.Column<Guid>(type: "uuid", nullable: false),
                    canal = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    asunto = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    cuerpo = table.Column<string>(type: "text", nullable: false),
                    estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plantillas_mensaje", x => x.id_plantilla);
                    table.ForeignKey(
                        name: "fk_plantillas_mensaje_programaciones_cobro_id_programacion",
                        column: x => x.id_programacion,
                        principalTable: "programaciones_cobro",
                        principalColumn: "id_programacion",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "viajes_ruta",
                columns: table => new
                {
                    id_viaje = table.Column<Guid>(type: "uuid", nullable: false),
                    id_ruta = table.Column<Guid>(type: "uuid", nullable: false),
                    id_asignacion = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha_viaje = table.Column<DateOnly>(type: "date", nullable: false),
                    trayecto = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    id_conductor = table.Column<Guid>(type: "uuid", nullable: false),
                    id_monitora = table.Column<Guid>(type: "uuid", nullable: true),
                    id_vehiculo = table.Column<Guid>(type: "uuid", nullable: false),
                    hora_inicio = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    hora_fin = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    id_iniciado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    nombre_ruta = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    jornada = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nombre_colegio = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    direccion_colegio = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    descripcion_vehiculo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    nombre_conductor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    nombre_monitora = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    id_orden_actualizado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    nombre_orden_actualizado_por = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    fecha_orden_actualizado = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    observaciones = table.Column<string>(type: "text", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_actualizacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_viajes_ruta", x => x.id_viaje);
                    table.ForeignKey(
                        name: "fk_viajes_ruta_asignaciones_ruta_id_asignacion",
                        column: x => x.id_asignacion,
                        principalTable: "asignaciones_ruta",
                        principalColumn: "id_asignacion",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_viajes_ruta_empleados_id_conductor",
                        column: x => x.id_conductor,
                        principalTable: "empleados",
                        principalColumn: "id_empleado",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_viajes_ruta_empleados_id_iniciado_por",
                        column: x => x.id_iniciado_por,
                        principalTable: "empleados",
                        principalColumn: "id_empleado",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_viajes_ruta_empleados_id_monitora",
                        column: x => x.id_monitora,
                        principalTable: "empleados",
                        principalColumn: "id_empleado",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_viajes_ruta_empleados_id_orden_actualizado_por",
                        column: x => x.id_orden_actualizado_por,
                        principalTable: "empleados",
                        principalColumn: "id_empleado",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_viajes_ruta_rutas_id_ruta",
                        column: x => x.id_ruta,
                        principalTable: "rutas",
                        principalColumn: "id_ruta",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_viajes_ruta_vehiculos_id_vehiculo",
                        column: x => x.id_vehiculo,
                        principalTable: "vehiculos",
                        principalColumn: "id_vehiculo",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "plantillas_mensaje_archivos",
                columns: table => new
                {
                    id_plantilla = table.Column<Guid>(type: "uuid", nullable: false),
                    id_archivo = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plantillas_mensaje_archivos", x => new { x.id_plantilla, x.id_archivo });
                    table.ForeignKey(
                        name: "fk_plantillas_mensaje_archivos_archivos_id_archivo",
                        column: x => x.id_archivo,
                        principalTable: "archivos",
                        principalColumn: "id_archivo",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_plantillas_mensaje_archivos_plantillas_mensaje_id_plantilla",
                        column: x => x.id_plantilla,
                        principalTable: "plantillas_mensaje",
                        principalColumn: "id_plantilla",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "eventos",
                columns: table => new
                {
                    id_evento = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    id_viaje = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_evento = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    descripcion = table.Column<string>(type: "text", nullable: true),
                    id_usuario = table.Column<Guid>(type: "uuid", nullable: true),
                    fecha_evento = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eventos", x => x.id_evento);
                    table.ForeignKey(
                        name: "fk_eventos_usuarios_id_usuario",
                        column: x => x.id_usuario,
                        principalTable: "usuarios",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_eventos_viajes_ruta_id_viaje",
                        column: x => x.id_viaje,
                        principalTable: "viajes_ruta",
                        principalColumn: "id_viaje",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "gps_vehiculos",
                columns: table => new
                {
                    id_gps = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    id_vehiculo = table.Column<Guid>(type: "uuid", nullable: false),
                    id_viaje = table.Column<Guid>(type: "uuid", nullable: false),
                    latitud = table.Column<decimal>(type: "numeric(9,6)", nullable: false),
                    longitud = table.Column<decimal>(type: "numeric(9,6)", nullable: false),
                    velocidad = table.Column<decimal>(type: "numeric(6,2)", nullable: true),
                    rumbo = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    fecha_registro = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gps_vehiculos", x => x.id_gps);
                    table.ForeignKey(
                        name: "fk_gps_vehiculos_vehiculos_id_vehiculo",
                        column: x => x.id_vehiculo,
                        principalTable: "vehiculos",
                        principalColumn: "id_vehiculo",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_gps_vehiculos_viajes_ruta_id_viaje",
                        column: x => x.id_viaje,
                        principalTable: "viajes_ruta",
                        principalColumn: "id_viaje",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "paradas_viaje",
                columns: table => new
                {
                    id_parada = table.Column<Guid>(type: "uuid", nullable: false),
                    id_viaje = table.Column<Guid>(type: "uuid", nullable: false),
                    id_estudiante = table.Column<Guid>(type: "uuid", nullable: false),
                    orden_recogida = table.Column<int>(type: "integer", nullable: false),
                    estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    hora_recogida = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    hora_entrega = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    foto_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    notas = table.Column<string>(type: "text", nullable: true),
                    nombre_estudiante = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    grado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    direccion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    nombre_acudiente = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    telefono_acudiente = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_paradas_viaje", x => x.id_parada);
                    table.ForeignKey(
                        name: "fk_paradas_viaje_estudiantes_id_estudiante",
                        column: x => x.id_estudiante,
                        principalTable: "estudiantes",
                        principalColumn: "id_estudiante",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_paradas_viaje_viajes_ruta_id_viaje",
                        column: x => x.id_viaje,
                        principalTable: "viajes_ruta",
                        principalColumn: "id_viaje",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_acudientes_id_empresa_tipo_documento_numero_documento",
                table: "acudientes",
                columns: new[] { "id_empresa", "tipo_documento", "numero_documento" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_acudientes_id_usuario",
                table: "acudientes",
                column: "id_usuario",
                unique: true,
                filter: "id_usuario IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_archivos_id_empresa",
                table: "archivos",
                column: "id_empresa");

            migrationBuilder.CreateIndex(
                name: "ix_archivos_subido_por",
                table: "archivos",
                column: "subido_por");

            migrationBuilder.CreateIndex(
                name: "ix_asignaciones_ruta_activa",
                table: "asignaciones_ruta",
                column: "id_ruta",
                unique: true,
                filter: "activo");

            migrationBuilder.CreateIndex(
                name: "ix_asignaciones_ruta_id_conductor",
                table: "asignaciones_ruta",
                column: "id_conductor");

            migrationBuilder.CreateIndex(
                name: "ix_asignaciones_ruta_id_empresa",
                table: "asignaciones_ruta",
                column: "id_empresa");

            migrationBuilder.CreateIndex(
                name: "ix_asignaciones_ruta_id_monitora",
                table: "asignaciones_ruta",
                column: "id_monitora");

            migrationBuilder.CreateIndex(
                name: "ix_asignaciones_ruta_id_ruta",
                table: "asignaciones_ruta",
                column: "id_ruta");

            migrationBuilder.CreateIndex(
                name: "ix_asignaciones_ruta_id_vehiculo",
                table: "asignaciones_ruta",
                column: "id_vehiculo");

            migrationBuilder.CreateIndex(
                name: "ix_auditoria_id_empresa_fecha_accion",
                table: "auditoria",
                columns: new[] { "id_empresa", "fecha_accion" });

            migrationBuilder.CreateIndex(
                name: "ix_auditoria_id_usuario",
                table: "auditoria",
                column: "id_usuario");

            migrationBuilder.CreateIndex(
                name: "ix_auditoria_tabla_afectada_id_registro",
                table: "auditoria",
                columns: new[] { "tabla_afectada", "id_registro" });

            migrationBuilder.CreateIndex(
                name: "ix_colegios_id_empresa_nit",
                table: "colegios",
                columns: new[] { "id_empresa", "nit" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_colegios_cursos_id_colegio_grado",
                table: "colegios_cursos",
                columns: new[] { "id_colegio", "grado" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_configuracion_sistema_id_empresa_clave",
                table: "configuracion_sistema",
                columns: new[] { "id_empresa", "clave" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cotizaciones_id_acudiente",
                table: "cotizaciones",
                column: "id_acudiente");

            migrationBuilder.CreateIndex(
                name: "ix_cotizaciones_id_empresa_numero",
                table: "cotizaciones",
                columns: new[] { "id_empresa", "numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cotizaciones_estudiantes_id_cotizacion",
                table: "cotizaciones_estudiantes",
                column: "id_cotizacion");

            migrationBuilder.CreateIndex(
                name: "ix_cotizaciones_estudiantes_id_estudiante",
                table: "cotizaciones_estudiantes",
                column: "id_estudiante");

            migrationBuilder.CreateIndex(
                name: "ix_documentos_vehiculo_fecha_vencimiento",
                table: "documentos_vehiculo",
                column: "fecha_vencimiento");

            migrationBuilder.CreateIndex(
                name: "ix_documentos_vehiculo_id_archivo",
                table: "documentos_vehiculo",
                column: "id_archivo");

            migrationBuilder.CreateIndex(
                name: "ix_documentos_vehiculo_id_vehiculo_tipo_documento",
                table: "documentos_vehiculo",
                columns: new[] { "id_vehiculo", "tipo_documento" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_empleados_id_archivo_contrato",
                table: "empleados",
                column: "id_archivo_contrato");

            migrationBuilder.CreateIndex(
                name: "ix_empleados_id_empresa_tipo_documento_numero_documento",
                table: "empleados",
                columns: new[] { "id_empresa", "tipo_documento", "numero_documento" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_empleados_id_usuario",
                table: "empleados",
                column: "id_usuario",
                unique: true,
                filter: "id_usuario IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_empresas_nit",
                table: "empresas",
                column: "nit",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_envios_programacion_id_mensaje_whatsapp",
                table: "envios_programacion",
                column: "id_mensaje_whatsapp");

            migrationBuilder.CreateIndex(
                name: "ix_envios_programacion_id_programacion_fecha",
                table: "envios_programacion",
                columns: new[] { "id_programacion", "fecha" });

            migrationBuilder.CreateIndex(
                name: "ix_estudiantes_id_colegio",
                table: "estudiantes",
                column: "id_colegio");

            migrationBuilder.CreateIndex(
                name: "ix_estudiantes_id_empresa_tipo_documento_numero_documento",
                table: "estudiantes",
                columns: new[] { "id_empresa", "tipo_documento", "numero_documento" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_estudiantes_acudientes_id_acudiente",
                table: "estudiantes_acudientes",
                column: "id_acudiente");

            migrationBuilder.CreateIndex(
                name: "ix_estudiantes_acudientes_principal",
                table: "estudiantes_acudientes",
                column: "id_estudiante",
                unique: true,
                filter: "es_principal");

            migrationBuilder.CreateIndex(
                name: "ix_eventos_id_usuario",
                table: "eventos",
                column: "id_usuario");

            migrationBuilder.CreateIndex(
                name: "ix_eventos_id_viaje_fecha_evento",
                table: "eventos",
                columns: new[] { "id_viaje", "fecha_evento" });

            migrationBuilder.CreateIndex(
                name: "ix_facturas_id_acudiente",
                table: "facturas",
                column: "id_acudiente");

            migrationBuilder.CreateIndex(
                name: "ix_facturas_id_empresa_numero_factura",
                table: "facturas",
                columns: new[] { "id_empresa", "numero_factura" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_facturas_id_estudiante",
                table: "facturas",
                column: "id_estudiante");

            migrationBuilder.CreateIndex(
                name: "ix_facturas_detalle_id_factura",
                table: "facturas_detalle",
                column: "id_factura");

            migrationBuilder.CreateIndex(
                name: "ix_gastos_creado_por",
                table: "gastos",
                column: "creado_por");

            migrationBuilder.CreateIndex(
                name: "ix_gastos_id_empresa_fecha_gasto",
                table: "gastos",
                columns: new[] { "id_empresa", "fecha_gasto" });

            migrationBuilder.CreateIndex(
                name: "ix_gastos_id_vehiculo",
                table: "gastos",
                column: "id_vehiculo");

            migrationBuilder.CreateIndex(
                name: "ix_gps_vehiculos_id_vehiculo",
                table: "gps_vehiculos",
                column: "id_vehiculo");

            migrationBuilder.CreateIndex(
                name: "ix_gps_vehiculos_id_viaje_fecha_registro",
                table: "gps_vehiculos",
                columns: new[] { "id_viaje", "fecha_registro" });

            migrationBuilder.CreateIndex(
                name: "ix_mensajes_whatsapp_id_acudiente",
                table: "mensajes_whatsapp",
                column: "id_acudiente");

            migrationBuilder.CreateIndex(
                name: "ix_mensajes_whatsapp_id_estudiante",
                table: "mensajes_whatsapp",
                column: "id_estudiante");

            migrationBuilder.CreateIndex(
                name: "ix_notificaciones_id_usuario_archivada_fecha_creacion",
                table: "notificaciones",
                columns: new[] { "id_usuario", "archivada", "fecha_creacion" });

            migrationBuilder.CreateIndex(
                name: "ix_notificaciones_id_usuario_clave",
                table: "notificaciones",
                columns: new[] { "id_usuario", "clave" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pagos_id_empresa_fecha_pago",
                table: "pagos",
                columns: new[] { "id_empresa", "fecha_pago" });

            migrationBuilder.CreateIndex(
                name: "ix_pagos_id_empresa_numero",
                table: "pagos",
                columns: new[] { "id_empresa", "numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pagos_id_estudiante",
                table: "pagos",
                column: "id_estudiante");

            migrationBuilder.CreateIndex(
                name: "ix_pagos_id_factura",
                table: "pagos",
                column: "id_factura");

            migrationBuilder.CreateIndex(
                name: "ix_pagos_vigente_por_periodo",
                table: "pagos",
                columns: new[] { "id_factura", "periodo" },
                unique: true,
                filter: "estado = 'VIGENTE' AND id_factura IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_paradas_viaje_id_estudiante",
                table: "paradas_viaje",
                column: "id_estudiante");

            migrationBuilder.CreateIndex(
                name: "ix_paradas_viaje_id_viaje_id_estudiante",
                table: "paradas_viaje",
                columns: new[] { "id_viaje", "id_estudiante" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_permisos_nombre",
                table: "permisos",
                column: "nombre",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plantillas_mensaje_en_uso",
                table: "plantillas_mensaje",
                columns: new[] { "id_programacion", "canal" },
                unique: true,
                filter: "estado = 'EN_USO'");

            migrationBuilder.CreateIndex(
                name: "ix_plantillas_mensaje_id_programacion",
                table: "plantillas_mensaje",
                column: "id_programacion");

            migrationBuilder.CreateIndex(
                name: "ix_plantillas_mensaje_archivos_id_archivo",
                table: "plantillas_mensaje_archivos",
                column: "id_archivo");

            migrationBuilder.CreateIndex(
                name: "ix_programaciones_cobro_id_empresa",
                table: "programaciones_cobro",
                column: "id_empresa");

            migrationBuilder.CreateIndex(
                name: "ix_programaciones_cobro_id_factura",
                table: "programaciones_cobro",
                column: "id_factura",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_roles_nombre",
                table: "roles",
                column: "nombre",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_roles_permisos_id_permiso",
                table: "roles_permisos",
                column: "id_permiso");

            migrationBuilder.CreateIndex(
                name: "ix_roles_permisos_id_rol_id_permiso",
                table: "roles_permisos",
                columns: new[] { "id_rol", "id_permiso" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rutas_id_colegio",
                table: "rutas",
                column: "id_colegio");

            migrationBuilder.CreateIndex(
                name: "ix_rutas_id_empresa_codigo",
                table: "rutas",
                columns: new[] { "id_empresa", "codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rutas_estudiantes_id_estudiante",
                table: "rutas_estudiantes",
                column: "id_estudiante");

            migrationBuilder.CreateIndex(
                name: "ix_rutas_estudiantes_id_ruta_id_estudiante",
                table: "rutas_estudiantes",
                columns: new[] { "id_ruta", "id_estudiante" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sesiones_usuario_id_reemplazada_por",
                table: "sesiones_usuario",
                column: "id_reemplazada_por");

            migrationBuilder.CreateIndex(
                name: "ix_sesiones_usuario_id_usuario",
                table: "sesiones_usuario",
                column: "id_usuario");

            migrationBuilder.CreateIndex(
                name: "ix_sesiones_usuario_token_hash",
                table: "sesiones_usuario",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_email",
                table: "usuarios",
                column: "email",
                unique: true,
                filter: "email IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_id_empresa",
                table: "usuarios",
                column: "id_empresa");

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_id_rol",
                table: "usuarios",
                column: "id_rol");

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_usuario",
                table: "usuarios",
                column: "usuario",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vehiculos_id_empresa_numero_interno",
                table: "vehiculos",
                columns: new[] { "id_empresa", "numero_interno" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vehiculos_id_empresa_placa",
                table: "vehiculos",
                columns: new[] { "id_empresa", "placa" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_viajes_ruta_en_curso",
                table: "viajes_ruta",
                column: "id_asignacion",
                unique: true,
                filter: "estado = 'EN_CURSO'");

            migrationBuilder.CreateIndex(
                name: "ix_viajes_ruta_id_asignacion_fecha_viaje_trayecto",
                table: "viajes_ruta",
                columns: new[] { "id_asignacion", "fecha_viaje", "trayecto" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_viajes_ruta_id_conductor",
                table: "viajes_ruta",
                column: "id_conductor");

            migrationBuilder.CreateIndex(
                name: "ix_viajes_ruta_id_iniciado_por",
                table: "viajes_ruta",
                column: "id_iniciado_por");

            migrationBuilder.CreateIndex(
                name: "ix_viajes_ruta_id_monitora",
                table: "viajes_ruta",
                column: "id_monitora");

            migrationBuilder.CreateIndex(
                name: "ix_viajes_ruta_id_orden_actualizado_por",
                table: "viajes_ruta",
                column: "id_orden_actualizado_por");

            migrationBuilder.CreateIndex(
                name: "ix_viajes_ruta_id_ruta_fecha_viaje",
                table: "viajes_ruta",
                columns: new[] { "id_ruta", "fecha_viaje" });

            migrationBuilder.CreateIndex(
                name: "ix_viajes_ruta_id_vehiculo",
                table: "viajes_ruta",
                column: "id_vehiculo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auditoria");

            migrationBuilder.DropTable(
                name: "colegios_cursos");

            migrationBuilder.DropTable(
                name: "configuracion_sistema");

            migrationBuilder.DropTable(
                name: "cotizaciones_estudiantes");

            migrationBuilder.DropTable(
                name: "documentos_vehiculo");

            migrationBuilder.DropTable(
                name: "envios_programacion");

            migrationBuilder.DropTable(
                name: "estudiantes_acudientes");

            migrationBuilder.DropTable(
                name: "eventos");

            migrationBuilder.DropTable(
                name: "facturas_detalle");

            migrationBuilder.DropTable(
                name: "gastos");

            migrationBuilder.DropTable(
                name: "gps_vehiculos");

            migrationBuilder.DropTable(
                name: "notificaciones");

            migrationBuilder.DropTable(
                name: "pagos");

            migrationBuilder.DropTable(
                name: "paradas_viaje");

            migrationBuilder.DropTable(
                name: "plantillas_mensaje_archivos");

            migrationBuilder.DropTable(
                name: "roles_permisos");

            migrationBuilder.DropTable(
                name: "rutas_estudiantes");

            migrationBuilder.DropTable(
                name: "sesiones_usuario");

            migrationBuilder.DropTable(
                name: "cotizaciones");

            migrationBuilder.DropTable(
                name: "mensajes_whatsapp");

            migrationBuilder.DropTable(
                name: "viajes_ruta");

            migrationBuilder.DropTable(
                name: "plantillas_mensaje");

            migrationBuilder.DropTable(
                name: "permisos");

            migrationBuilder.DropTable(
                name: "asignaciones_ruta");

            migrationBuilder.DropTable(
                name: "programaciones_cobro");

            migrationBuilder.DropTable(
                name: "empleados");

            migrationBuilder.DropTable(
                name: "rutas");

            migrationBuilder.DropTable(
                name: "vehiculos");

            migrationBuilder.DropTable(
                name: "facturas");

            migrationBuilder.DropTable(
                name: "archivos");

            migrationBuilder.DropTable(
                name: "acudientes");

            migrationBuilder.DropTable(
                name: "estudiantes");

            migrationBuilder.DropTable(
                name: "usuarios");

            migrationBuilder.DropTable(
                name: "colegios");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropTable(
                name: "empresas");
        }
    }
}
