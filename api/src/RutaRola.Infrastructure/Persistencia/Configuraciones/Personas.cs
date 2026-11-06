using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RutaRola.Domain.Personas;
using static RutaRola.Infrastructure.Persistencia.Configuraciones.Extensiones;

namespace RutaRola.Infrastructure.Persistencia.Configuraciones;

internal class EmpleadoConfig : IEntityTypeConfiguration<Empleado>
{
    public void Configure(EntityTypeBuilder<Empleado> b)
    {
        b.Tabla("empleados", "id_empleado");
        b.CamposPersona();
        b.Property(x => x.Telefono).HasMaxLength(20);
        b.Property(x => x.Correo).HasMaxLength(150);
        b.Property(x => x.Direccion).HasMaxLength(200);
        b.Property(x => x.Sueldo).HasColumnType(Dinero);
        b.Property(x => x.NumeroLicencia).HasMaxLength(30);
        b.Property(x => x.CategoriaLicencia).HasMaxLength(5);
        b.HasOne(x => x.Usuario).WithMany().HasForeignKey(x => x.IdUsuario);
        b.HasIndex(x => x.IdUsuario).IsUnique().HasFilter("id_usuario IS NOT NULL");
        b.HasOne(x => x.ArchivoContrato).WithMany().HasForeignKey(x => x.IdArchivoContrato);
    }
}

internal class AcudienteConfig : IEntityTypeConfiguration<Acudiente>
{
    public void Configure(EntityTypeBuilder<Acudiente> b)
    {
        b.Tabla("acudientes", "id_acudiente");
        b.CamposPersona();
        b.Property(x => x.Telefono).HasMaxLength(20);
        b.Property(x => x.TelefonoAlterno).HasMaxLength(20);
        b.Property(x => x.Correo).HasMaxLength(150);
        b.Property(x => x.Direccion).HasMaxLength(200);
        b.Property(x => x.Parentesco).HasMaxLength(30);
        b.HasOne(x => x.Usuario).WithMany().HasForeignKey(x => x.IdUsuario);
        b.HasIndex(x => x.IdUsuario).IsUnique().HasFilter("id_usuario IS NOT NULL");
    }
}

internal class EstudianteConfig : IEntityTypeConfiguration<Estudiante>
{
    public void Configure(EntityTypeBuilder<Estudiante> b)
    {
        b.Tabla("estudiantes", "id_estudiante");
        b.CamposPersona();
        b.Property(x => x.Direccion).HasMaxLength(200);
        b.Property(x => x.Latitud).HasColumnType(Coordenada);
        b.Property(x => x.Longitud).HasColumnType(Coordenada);
        b.Property(x => x.Grado).HasMaxLength(20);
        b.Property(x => x.Jornada).HasMaxLength(20);
        b.HasOne(x => x.Colegio).WithMany().HasForeignKey(x => x.IdColegio);
    }
}

internal class EstudianteAcudienteConfig : IEntityTypeConfiguration<EstudianteAcudiente>
{
    public void Configure(EntityTypeBuilder<EstudianteAcudiente> b)
    {
        b.ToTable("estudiantes_acudientes");
        b.HasKey(x => new { x.IdEstudiante, x.IdAcudiente });
        b.HasOne(x => x.Estudiante).WithMany(e => e.Acudientes).HasForeignKey(x => x.IdEstudiante).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Acudiente).WithMany(a => a.Estudiantes).HasForeignKey(x => x.IdAcudiente);
        // Un solo acudiente principal por estudiante.
        b.HasIndex(x => x.IdEstudiante).IsUnique().HasFilter("es_principal").HasDatabaseName("ix_estudiantes_acudientes_principal");
        b.HasIndex(x => x.IdAcudiente);
    }
}
