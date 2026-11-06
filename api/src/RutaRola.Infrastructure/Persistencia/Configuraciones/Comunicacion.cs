using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RutaRola.Domain.Comunicacion;

namespace RutaRola.Infrastructure.Persistencia.Configuraciones;

internal class NotificacionConfig : IEntityTypeConfiguration<Notificacion>
{
    public void Configure(EntityTypeBuilder<Notificacion> b)
    {
        b.ToTable("notificaciones");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id_notificacion").ValueGeneratedNever();
        b.Property(x => x.Clave).HasMaxLength(200);
        b.Property(x => x.Titulo).HasMaxLength(200);
        b.Property(x => x.Enlace).HasMaxLength(300);
        b.Property(x => x.TextoEnlace).HasMaxLength(100);
        b.HasOne(x => x.Usuario).WithMany().HasForeignKey(x => x.IdUsuario).OnDelete(DeleteBehavior.Cascade);
        // Un mismo hecho se notifica una sola vez por usuario (aunque se elimine).
        b.HasIndex(x => new { x.IdUsuario, x.Clave }).IsUnique();
        b.HasIndex(x => new { x.IdUsuario, x.Archivada, x.FechaCreacion });
    }
}

internal class MensajeWhatsappConfig : IEntityTypeConfiguration<MensajeWhatsapp>
{
    public void Configure(EntityTypeBuilder<MensajeWhatsapp> b)
    {
        b.ToTable("mensajes_whatsapp");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id_mensaje").ValueGeneratedNever();
        b.Property(x => x.Telefono).HasMaxLength(20);
        b.Property(x => x.IdExterno).HasMaxLength(100);
        b.HasOne(x => x.Acudiente).WithMany().HasForeignKey(x => x.IdAcudiente);
        b.HasOne(x => x.Estudiante).WithMany().HasForeignKey(x => x.IdEstudiante);
    }
}

internal class ArchivoConfig : IEntityTypeConfiguration<Archivo>
{
    public void Configure(EntityTypeBuilder<Archivo> b)
    {
        b.Tabla("archivos", "id_archivo");
        b.Property(x => x.Nombre).HasMaxLength(255);
        b.Property(x => x.TipoArchivo).HasMaxLength(100);
        b.Property(x => x.UrlArchivo).HasMaxLength(500);
        b.HasOne(x => x.Usuario).WithMany().HasForeignKey(x => x.SubidoPor);
    }
}
