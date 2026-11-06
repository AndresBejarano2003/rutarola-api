using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using RutaRola.Application.Comun;
using RutaRola.Domain.Comun;
using RutaRola.Domain.Comunicacion;
using RutaRola.Domain.Enums;
using RutaRola.Domain.Finanzas;
using RutaRola.Domain.Operacion;
using RutaRola.Domain.Personas;
using RutaRola.Domain.Seguridad;

namespace RutaRola.Infrastructure.Persistencia;

/// <summary>
/// Multiempresa: toda entidad <see cref="IEntidadEmpresa"/> (y sus hijas, a través del padre) se
/// filtra por la empresa del token con el query filter <see cref="FiltroEmpresa"/>. Sin empresa
/// (anónimo, SuperAdmin) no se ve nada, salvo en <see cref="ModoSistema"/> (seed, procesos del servidor)
/// o con <c>IgnoreQueryFilters([FiltroEmpresa])</c> explícito.
/// </summary>
public class RutaRolaDbContext(
    DbContextOptions<RutaRolaDbContext> options,
    IContextoUsuario contexto,
    TimeProvider reloj) : DbContext(options), IBaseDatos
{
    public const string FiltroEmpresa = FiltrosConsulta.Empresa;

    /// <summary>Empresa del token; <see cref="Guid.Empty"/> si no hay (no coincide con ningún registro).</summary>
    public Guid IdEmpresaActual => contexto.IdEmpresa ?? Guid.Empty;

    /// <summary>Desactiva el filtro por empresa en este contexto. Solo para seed y procesos del servidor.</summary>
    public bool ModoSistema { get; private set; }

    public void UsarModoSistema() => ModoSistema = true;

    // Seguridad
    public DbSet<Empresa> Empresas => Set<Empresa>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Rol> Roles => Set<Rol>();
    public DbSet<Permiso> Permisos => Set<Permiso>();
    public DbSet<RolPermiso> RolesPermisos => Set<RolPermiso>();
    public DbSet<SesionUsuario> SesionesUsuario => Set<SesionUsuario>();
    public DbSet<Auditoria> Auditoria => Set<Auditoria>();
    public DbSet<ConfiguracionSistema> ConfiguracionSistema => Set<ConfiguracionSistema>();
    // Personas
    public DbSet<Empleado> Empleados => Set<Empleado>();
    public DbSet<Acudiente> Acudientes => Set<Acudiente>();
    public DbSet<Estudiante> Estudiantes => Set<Estudiante>();
    public DbSet<EstudianteAcudiente> EstudiantesAcudientes => Set<EstudianteAcudiente>();
    // Operación
    public DbSet<Colegio> Colegios => Set<Colegio>();
    public DbSet<ColegioCurso> ColegiosCursos => Set<ColegioCurso>();
    public DbSet<Vehiculo> Vehiculos => Set<Vehiculo>();
    public DbSet<DocumentoVehiculo> DocumentosVehiculo => Set<DocumentoVehiculo>();
    public DbSet<Ruta> Rutas => Set<Ruta>();
    public DbSet<RutaEstudiante> RutasEstudiantes => Set<RutaEstudiante>();
    public DbSet<AsignacionRuta> AsignacionesRuta => Set<AsignacionRuta>();
    public DbSet<ViajeRuta> ViajesRuta => Set<ViajeRuta>();
    public DbSet<ParadaViaje> ParadasViaje => Set<ParadaViaje>();
    public DbSet<Evento> Eventos => Set<Evento>();
    public DbSet<GpsVehiculo> GpsVehiculos => Set<GpsVehiculo>();
    // Finanzas
    public DbSet<Factura> Facturas => Set<Factura>();
    public DbSet<FacturaDetalle> FacturasDetalle => Set<FacturaDetalle>();
    public DbSet<Pago> Pagos => Set<Pago>();
    public DbSet<Cotizacion> Cotizaciones => Set<Cotizacion>();
    public DbSet<CotizacionEstudiante> CotizacionesEstudiantes => Set<CotizacionEstudiante>();
    public DbSet<Gasto> Gastos => Set<Gasto>();
    public DbSet<ProgramacionCobro> ProgramacionesCobro => Set<ProgramacionCobro>();
    public DbSet<PlantillaMensaje> PlantillasMensaje => Set<PlantillaMensaje>();
    public DbSet<PlantillaMensajeArchivo> PlantillasMensajeArchivos => Set<PlantillaMensajeArchivo>();
    public DbSet<EnvioProgramacion> EnviosProgramacion => Set<EnvioProgramacion>();
    // Comunicación
    public DbSet<Notificacion> Notificaciones => Set<Notificacion>();
    public DbSet<MensajeWhatsapp> MensajesWhatsapp => Set<MensajeWhatsapp>();
    public DbSet<Archivo> Archivos => Set<Archivo>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Sin borrado en cascada por defecto: se declara explícitamente solo en hijos de composición.
        builder.Conventions.Remove<CascadeDeleteConvention>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RutaRolaDbContext).Assembly);
        // Creada en la migración Fase2Autenticacion (extensión unaccent).
        modelBuilder.HasDbFunction(typeof(FuncionesBd).GetMethod(nameof(FuncionesBd.SinTildes))!).HasName("f_sin_tildes").HasSchema("public");

        foreach (var tipo in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(IEntidadEmpresa).IsAssignableFrom(tipo.ClrType))
            {
                modelBuilder.Entity(tipo.ClrType)
                    .HasOne(typeof(Empresa))
                    .WithMany()
                    .HasForeignKey(nameof(IEntidadEmpresa.IdEmpresa))
                    .OnDelete(DeleteBehavior.Restrict);
                GetType().GetMethod(nameof(FiltrarPorEmpresa), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .MakeGenericMethod(tipo.ClrType)
                    .Invoke(this, [modelBuilder]);
            }

            foreach (var propiedad in tipo.GetProperties())
            {
                var clr = Nullable.GetUnderlyingType(propiedad.ClrType) ?? propiedad.ClrType;
                if (clr.IsEnum)
                {
                    propiedad.SetValueConverter((Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter)
                        Activator.CreateInstance(typeof(ConvertidorEnum<>).MakeGenericType(clr))!);
                    propiedad.SetMaxLength(30);
                }
            }

            foreach (var fk in tipo.GetForeignKeys().Where(f => f.DeleteBehavior == DeleteBehavior.ClientSetNull))
            {
                fk.DeleteBehavior = DeleteBehavior.Restrict;
            }
        }

        // Tablas sin id_empresa: se filtran por la empresa del registro padre.
        modelBuilder.Entity<Empresa>().HasQueryFilter(FiltroEmpresa, e => ModoSistema || e.Id == IdEmpresaActual);
        modelBuilder.Entity<Usuario>().HasQueryFilter(FiltroEmpresa, u => ModoSistema || u.IdEmpresa == IdEmpresaActual);
        modelBuilder.Entity<SesionUsuario>().HasQueryFilter(FiltroEmpresa, s => ModoSistema || s.Usuario.IdEmpresa == IdEmpresaActual);
        modelBuilder.Entity<Auditoria>().HasQueryFilter(FiltroEmpresa, a => ModoSistema || a.IdEmpresa == IdEmpresaActual);
        modelBuilder.Entity<EstudianteAcudiente>().HasQueryFilter(FiltroEmpresa, x => ModoSistema || x.Estudiante.IdEmpresa == IdEmpresaActual);
        modelBuilder.Entity<ColegioCurso>().HasQueryFilter(FiltroEmpresa, x => ModoSistema || x.Colegio.IdEmpresa == IdEmpresaActual);
        modelBuilder.Entity<DocumentoVehiculo>().HasQueryFilter(FiltroEmpresa, x => ModoSistema || x.Vehiculo.IdEmpresa == IdEmpresaActual);
        modelBuilder.Entity<RutaEstudiante>().HasQueryFilter(FiltroEmpresa, x => ModoSistema || x.Ruta.IdEmpresa == IdEmpresaActual);
        modelBuilder.Entity<ViajeRuta>().HasQueryFilter(FiltroEmpresa, x => ModoSistema || x.Ruta.IdEmpresa == IdEmpresaActual);
        modelBuilder.Entity<ParadaViaje>().HasQueryFilter(FiltroEmpresa, x => ModoSistema || x.Viaje.Ruta.IdEmpresa == IdEmpresaActual);
        modelBuilder.Entity<Evento>().HasQueryFilter(FiltroEmpresa, x => ModoSistema || x.Viaje.Ruta.IdEmpresa == IdEmpresaActual);
        modelBuilder.Entity<GpsVehiculo>().HasQueryFilter(FiltroEmpresa, x => ModoSistema || x.Vehiculo.IdEmpresa == IdEmpresaActual);
        modelBuilder.Entity<FacturaDetalle>().HasQueryFilter(FiltroEmpresa, x => ModoSistema || x.Factura.IdEmpresa == IdEmpresaActual);
        modelBuilder.Entity<CotizacionEstudiante>().HasQueryFilter(FiltroEmpresa, x => ModoSistema || x.Cotizacion.IdEmpresa == IdEmpresaActual);
        modelBuilder.Entity<PlantillaMensaje>().HasQueryFilter(FiltroEmpresa, x => ModoSistema || x.Programacion.IdEmpresa == IdEmpresaActual);
        modelBuilder.Entity<PlantillaMensajeArchivo>().HasQueryFilter(FiltroEmpresa, x => ModoSistema || x.Plantilla.Programacion.IdEmpresa == IdEmpresaActual);
        modelBuilder.Entity<EnvioProgramacion>().HasQueryFilter(FiltroEmpresa, x => ModoSistema || x.Programacion.IdEmpresa == IdEmpresaActual);
        modelBuilder.Entity<Notificacion>().HasQueryFilter(FiltroEmpresa, x => ModoSistema || x.Usuario.IdEmpresa == IdEmpresaActual);
        modelBuilder.Entity<MensajeWhatsapp>().HasQueryFilter(FiltroEmpresa, x => ModoSistema || x.Acudiente.IdEmpresa == IdEmpresaActual);
    }

    private void FiltrarPorEmpresa<T>(ModelBuilder modelBuilder) where T : class, IEntidadEmpresa
    {
        Expression<Func<T, bool>> filtro = e => ModoSistema || e.IdEmpresa == IdEmpresaActual;
        modelBuilder.Entity<T>().HasQueryFilter(FiltroEmpresa, filtro);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PrepararCambios();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        PrepararCambios();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Asigna la empresa del token a lo que se crea, impide tocar registros de otra empresa (o
    /// cambiarles la empresa) y pone las fechas de creación/actualización en UTC.
    /// </summary>
    private void PrepararCambios()
    {
        var ahora = reloj.GetUtcNow().UtcDateTime;
        AsignarEmpresaYFechas(ahora);
        RegistrarAuditoria(ahora);
    }

    /// <summary>Tablas cuyas creaciones, cambios y eliminaciones quedan en <c>auditoria</c>.</summary>
    private static readonly HashSet<Type> Auditadas =
        [typeof(Estudiante), typeof(EstudianteAcudiente), typeof(Acudiente), typeof(Empleado), typeof(Usuario), typeof(Pago), typeof(Factura)];

    /// <summary>Campos que no se copian a la auditoría (técnicos o secretos).</summary>
    private static readonly HashSet<string> NoAuditados =
        [nameof(Entidad.FechaCreacion), nameof(Entidad.FechaActualizacion), nameof(Usuario.UltimoLogin), nameof(Usuario.PasswordHash), "Version"];

    private static readonly JsonSerializerOptions JsonAuditoria = new()
    {
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper) },
    };

    private void RegistrarAuditoria(DateTime ahora)
    {
        var registros = new List<Auditoria>();
        foreach (var entrada in ChangeTracker.Entries())
        {
            if (entrada.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)
                || !Auditadas.Contains(entrada.Metadata.ClrType))
            {
                continue;
            }

            var campos = entrada.Properties.Where(p => !NoAuditados.Contains(p.Metadata.Name)).ToList();
            var claveCambiada = entrada.State == EntityState.Modified
                && entrada.Properties.Any(p => p.Metadata.Name == nameof(Usuario.PasswordHash) && p.IsModified && !Equals(p.OriginalValue, p.CurrentValue));
            if (entrada.State == EntityState.Modified)
            {
                campos = campos.Where(p => p.IsModified && !Equals(p.OriginalValue, p.CurrentValue)).ToList();
                if (campos.Count == 0 && !claveCambiada)
                {
                    continue; // p. ej. solo cambió ultimo_login.
                }
            }

            var anterior = entrada.State == EntityState.Added ? null : Valores(campos, original: true);
            var nuevo = entrada.State == EntityState.Deleted ? null : Valores(campos, original: false);
            if (claveCambiada)
            {
                nuevo!["contrasena"] = "(cambiada)";
            }

            registros.Add(new Auditoria
            {
                IdEmpresa = entrada.Entity switch
                {
                    IEntidadEmpresa e => e.IdEmpresa,
                    Usuario u => u.IdEmpresa,
                    _ => contexto.IdEmpresa,
                },
                IdUsuario = contexto.IdUsuario,
                Accion = entrada.State switch
                {
                    EntityState.Added => AccionAuditoria.Crear,
                    EntityState.Deleted => AccionAuditoria.Eliminar,
                    _ => AccionAuditoria.Actualizar,
                },
                TablaAfectada = entrada.Metadata.GetTableName() ?? entrada.Metadata.ClrType.Name,
                IdRegistro = entrada.Entity switch
                {
                    Entidad e => e.Id,
                    EstudianteAcudiente ea => ea.IdEstudiante,
                    _ => Guid.Empty,
                },
                ValorAnterior = anterior is null ? null : JsonSerializer.Serialize(anterior, JsonAuditoria),
                ValorNuevo = nuevo is null ? null : JsonSerializer.Serialize(nuevo, JsonAuditoria),
                FechaAccion = ahora,
                DireccionIp = contexto.DireccionIp,
            });
        }
        Auditoria.AddRange(registros);
    }

    private static Dictionary<string, object?> Valores(IEnumerable<PropertyEntry> campos, bool original) =>
        campos.ToDictionary(p => p.Metadata.Name, p => original ? p.OriginalValue : p.CurrentValue);

    private void AsignarEmpresaYFechas(DateTime ahora)
    {

        foreach (var entrada in ChangeTracker.Entries())
        {
            if (entrada.Entity is IEntidadEmpresa conEmpresa && !ModoSistema)
            {
                switch (entrada.State)
                {
                    case EntityState.Added:
                        if (contexto.IdEmpresa is not { } empresa)
                        {
                            throw new ErrorNegocio("Se requiere una empresa para crear este registro.", CodigoError.Prohibido);
                        }
                        conEmpresa.IdEmpresa = empresa;
                        break;
                    case EntityState.Modified or EntityState.Deleted:
                        var original = (Guid)entrada.OriginalValues[nameof(IEntidadEmpresa.IdEmpresa)]!;
                        if (original != IdEmpresaActual || conEmpresa.IdEmpresa != original)
                        {
                            throw new ErrorNegocio("No tienes permisos para modificar este registro.", CodigoError.Prohibido);
                        }
                        break;
                }
            }

            if (entrada.Entity is Entidad entidad)
            {
                if (entrada.State == EntityState.Added && entidad.FechaCreacion == default)
                {
                    entidad.FechaCreacion = ahora;
                }
                else if (entrada.State == EntityState.Modified)
                {
                    entidad.FechaActualizacion = ahora;
                }
            }
            else if (entrada.State == EntityState.Added)
            {
                var fecha = entrada.Metadata.FindProperty("FechaCreacion");
                if (fecha is not null && (DateTime)entrada.CurrentValues[fecha]! == default)
                {
                    entrada.CurrentValues[fecha] = ahora;
                }
            }
        }
    }
}
