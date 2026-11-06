using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RutaRola.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Fase2Autenticacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "sesiones_usuario",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            // Búsquedas sin tildes ni mayúsculas (FuncionesBd.SinTildes), como normalizar() del frontend.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS unaccent;");
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION public.f_sin_tildes(texto text) RETURNS text
                LANGUAGE sql IMMUTABLE PARALLEL SAFE STRICT
                AS $$ SELECT lower(public.unaccent('public.unaccent'::regdictionary, texto)) $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.f_sin_tildes(text);");
            migrationBuilder.DropColumn(
                name: "xmin",
                table: "sesiones_usuario");
        }
    }
}
