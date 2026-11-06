using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RutaRola.Domain.Operacion;
using static RutaRola.Infrastructure.Persistencia.Configuraciones.Extensiones;

namespace RutaRola.Infrastructure.Persistencia.Configuraciones;

internal class ColegioConfig : IEntityTypeConfiguration<Colegio>
{
    public void Configure(EntityTypeBuilder<Colegio> b)
    {
        b.Tabla("colegios", "id_colegio");
        b.Property(x => x.Nit).HasMaxLength(20);
        b.Property(x => x.RazonSocial).HasMaxLength(150);
        b.Property(x => x.Direccion).HasMaxLength(200);
        b.Property(x => x.Telefono).HasMaxLength(20);
        b.Property(x => x.PersonaContacto).HasMaxLength(150);
        b.Property(x => x.Latitud).HasColumnType(Coordenada);
        b.Property(x => x.Longitud).HasColumnType(Coordenada);
        b.HasIndex(x => new { x.IdEmpresa, x.Nit }).IsUnique();
    }
}

internal class ColegioCursoConfig : IEntityTypeConfiguration<ColegioCurso>
{
    public void Configure(EntityTypeBuilder<ColegioCurso> b)
    {
        b.ToTable("colegios_cursos");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id_curso").ValueGeneratedNever();
        b.Property(x => x.Grado).HasMaxLength(20);
        b.HasOne(x => x.Colegio).WithMany(c => c.Cursos).HasForeignKey(x => x.IdColegio).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.IdColegio, x.Grado }).IsUnique();
    }
}

internal class VehiculoConfig : IEntityTypeConfiguration<Vehiculo>
{
    public void Configure(EntityTypeBuilder<Vehiculo> b)
    {
        b.Tabla("vehiculos", "id_vehiculo");
        b.Property(x => x.Placa).HasMaxLength(10);
        b.Property(x => x.NumeroInterno).HasMaxLength(20);
        b.Property(x => x.Marca).HasMaxLength(50);
        b.Property(x => x.Linea).HasMaxLength(50);
        b.Property(x => x.Color).HasMaxLength(30);
        b.HasIndex(x => new { x.IdEmpresa, x.Placa }).IsUnique();
        b.HasIndex(x => new { x.IdEmpresa, x.NumeroInterno }).IsUnique();
    }
}

internal class DocumentoVehiculoConfig : IEntityTypeConfiguration<DocumentoVehiculo>
{
    public void Configure(EntityTypeBuilder<DocumentoVehiculo> b)
    {
        b.ToTable("documentos_vehiculo");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id_documento").ValueGeneratedNever();
        b.Property(x => x.NumeroDocumento).HasMaxLength(50);
        b.HasOne(x => x.Vehiculo).WithMany(v => v.Documentos).HasForeignKey(x => x.IdVehiculo).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Archivo).WithMany().HasForeignKey(x => x.IdArchivo);
        b.HasIndex(x => new { x.IdVehiculo, x.TipoDocumento }).IsUnique();
        b.HasIndex(x => x.FechaVencimiento);
    }
}

internal class RutaConfig : IEntityTypeConfiguration<Ruta>
{
    public void Configure(EntityTypeBuilder<Ruta> b)
    {
        b.Tabla("rutas", "id_ruta");
        b.Property(x => x.Codigo).HasMaxLength(20);
        b.Property(x => x.Nombre).HasMaxLength(100);
        b.Property(x => x.Jornada).HasMaxLength(20);
        b.Property(x => x.Sector).HasMaxLength(100);
        b.HasOne(x => x.Colegio).WithMany().HasForeignKey(x => x.IdColegio);
        b.HasIndex(x => new { x.IdEmpresa, x.Codigo }).IsUnique();
    }
}

internal class RutaEstudianteConfig : IEntityTypeConfiguration<RutaEstudiante>
{
    public void Configure(EntityTypeBuilder<RutaEstudiante> b)
    {
        b.ToTable("rutas_estudiantes");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id_ruta_estudiante").ValueGeneratedNever();
        b.HasOne(x => x.Ruta).WithMany(r => r.Estudiantes).HasForeignKey(x => x.IdRuta).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Estudiante).WithMany().HasForeignKey(x => x.IdEstudiante);
        b.HasIndex(x => new { x.IdRuta, x.IdEstudiante }).IsUnique();
        b.HasIndex(x => x.IdEstudiante);
    }
}

internal class AsignacionRutaConfig : IEntityTypeConfiguration<AsignacionRuta>
{
    public void Configure(EntityTypeBuilder<AsignacionRuta> b)
    {
        b.Tabla("asignaciones_ruta", "id_asignacion");
        b.HasOne(x => x.Ruta).WithMany().HasForeignKey(x => x.IdRuta);
        b.HasOne(x => x.Vehiculo).WithMany().HasForeignKey(x => x.IdVehiculo);
        b.HasOne(x => x.Conductor).WithMany().HasForeignKey(x => x.IdConductor);
        b.HasOne(x => x.Monitora).WithMany().HasForeignKey(x => x.IdMonitora);
        // Una sola asignación activa por ruta.
        b.HasIndex(x => x.IdRuta, "activa").IsUnique().HasFilter("activo").HasDatabaseName("ix_asignaciones_ruta_activa");
        b.HasIndex(x => x.IdRuta);
    }
}

internal class ViajeRutaConfig : IEntityTypeConfiguration<ViajeRuta>
{
    public void Configure(EntityTypeBuilder<ViajeRuta> b)
    {
        b.Tabla("viajes_ruta", "id_viaje");
        b.Property(x => x.Version).IsRowVersion();
        b.Property(x => x.NombreRuta).HasMaxLength(150);
        b.Property(x => x.Jornada).HasMaxLength(20);
        b.Property(x => x.NombreColegio).HasMaxLength(150);
        b.Property(x => x.DireccionColegio).HasMaxLength(200);
        b.Property(x => x.DescripcionVehiculo).HasMaxLength(100);
        b.Property(x => x.NombreConductor).HasMaxLength(200);
        b.Property(x => x.NombreMonitora).HasMaxLength(200);
        b.Property(x => x.NombreOrdenActualizadoPor).HasMaxLength(200);
        b.HasOne(x => x.Ruta).WithMany().HasForeignKey(x => x.IdRuta);
        b.HasOne(x => x.Asignacion).WithMany().HasForeignKey(x => x.IdAsignacion);
        b.HasOne(x => x.Conductor).WithMany().HasForeignKey(x => x.IdConductor);
        b.HasOne(x => x.Monitora).WithMany().HasForeignKey(x => x.IdMonitora);
        b.HasOne(x => x.Vehiculo).WithMany().HasForeignKey(x => x.IdVehiculo);
        b.HasOne(x => x.IniciadoPor).WithMany().HasForeignKey(x => x.IdIniciadoPor);
        b.HasOne<Domain.Personas.Empleado>().WithMany().HasForeignKey(x => x.IdOrdenActualizadoPor);
        b.HasIndex(x => new { x.IdRuta, x.FechaViaje });
        // Un recorrido por asignación + fecha + trayecto, y uno solo EN_CURSO por asignación.
        b.HasIndex(x => new { x.IdAsignacion, x.FechaViaje, x.Trayecto }).IsUnique();
        b.HasIndex(x => x.IdAsignacion).IsUnique().HasFilter("estado = 'EN_CURSO'").HasDatabaseName("ix_viajes_ruta_en_curso");
    }
}

internal class ParadaViajeConfig : IEntityTypeConfiguration<ParadaViaje>
{
    public void Configure(EntityTypeBuilder<ParadaViaje> b)
    {
        b.ToTable("paradas_viaje");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id_parada").ValueGeneratedNever();
        b.Property(x => x.FotoUrl).HasMaxLength(500);
        b.Property(x => x.NombreEstudiante).HasMaxLength(200);
        b.Property(x => x.Grado).HasMaxLength(20);
        b.Property(x => x.Direccion).HasMaxLength(200);
        b.Property(x => x.NombreAcudiente).HasMaxLength(200);
        b.Property(x => x.TelefonoAcudiente).HasMaxLength(20);
        b.HasOne(x => x.Viaje).WithMany(v => v.Paradas).HasForeignKey(x => x.IdViaje).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Estudiante).WithMany().HasForeignKey(x => x.IdEstudiante);
        b.HasIndex(x => new { x.IdViaje, x.IdEstudiante }).IsUnique();
        b.HasIndex(x => x.IdEstudiante);
    }
}

internal class EventoConfig : IEntityTypeConfiguration<Evento>
{
    public void Configure(EntityTypeBuilder<Evento> b)
    {
        b.ToTable("eventos");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id_evento").UseIdentityAlwaysColumn();
        b.HasOne(x => x.Viaje).WithMany(v => v.Eventos).HasForeignKey(x => x.IdViaje).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Usuario).WithMany().HasForeignKey(x => x.IdUsuario);
        b.HasIndex(x => new { x.IdViaje, x.FechaEvento });
    }
}

internal class GpsVehiculoConfig : IEntityTypeConfiguration<GpsVehiculo>
{
    public void Configure(EntityTypeBuilder<GpsVehiculo> b)
    {
        b.ToTable("gps_vehiculos");
        b.HasKey(x => x.IdGps);
        b.Property(x => x.IdGps).HasColumnName("id_gps").UseIdentityAlwaysColumn();
        b.Property(x => x.Latitud).HasColumnType(Coordenada);
        b.Property(x => x.Longitud).HasColumnType(Coordenada);
        b.Property(x => x.Velocidad).HasColumnType("numeric(6,2)");
        b.Property(x => x.Rumbo).HasColumnType("numeric(5,2)");
        b.HasOne(x => x.Vehiculo).WithMany().HasForeignKey(x => x.IdVehiculo);
        b.HasOne(x => x.Viaje).WithMany().HasForeignKey(x => x.IdViaje);
        b.HasIndex(x => new { x.IdViaje, x.FechaRegistro });
        b.HasIndex(x => x.IdVehiculo);
    }
}
