using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RutaRola.Domain.Comun;

namespace RutaRola.Infrastructure.Persistencia.Configuraciones;

internal static class Extensiones
{
    public const string Dinero = "numeric(14,2)";
    public const string Coordenada = "numeric(9,6)";

    /// <summary>Tabla y llave primaria con el nombre del modelo (<c>id_estudiante</c>…).</summary>
    public static EntityTypeBuilder<T> Tabla<T>(this EntityTypeBuilder<T> b, string tabla, string columnaId) where T : Entidad
    {
        b.ToTable(tabla);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName(columnaId).ValueGeneratedNever();
        return b;
    }

    /// <summary>Campos de identificación de <see cref="Persona"/> + documento único por empresa.</summary>
    public static void CamposPersona<T>(this EntityTypeBuilder<T> b) where T : Persona
    {
        b.Property(x => x.TipoDocumento).HasMaxLength(10);
        b.Property(x => x.NumeroDocumento).HasMaxLength(15);
        b.Property(x => x.PrimerNombre).HasMaxLength(50);
        b.Property(x => x.SegundoNombre).HasMaxLength(50);
        b.Property(x => x.PrimerApellido).HasMaxLength(50);
        b.Property(x => x.SegundoApellido).HasMaxLength(50);
        b.Property(x => x.Foto).HasColumnName("foto");
        b.HasIndex(x => new { x.IdEmpresa, x.TipoDocumento, x.NumeroDocumento }).IsUnique();
    }
}
