using Microsoft.EntityFrameworkCore;
using RutaRola.Domain.Comunicacion;
using RutaRola.Domain.Finanzas;
using RutaRola.Domain.Operacion;
using RutaRola.Domain.Personas;
using RutaRola.Domain.Seguridad;

namespace RutaRola.Application.Comun;

/// <summary>
/// Acceso a datos para los servicios (lo implementa <c>RutaRolaDbContext</c>). Todas las consultas
/// pasan por el filtro de empresa <see cref="FiltrosConsulta.Empresa"/>.
/// </summary>
public interface IBaseDatos
{
    DbSet<Empresa> Empresas { get; }
    DbSet<Usuario> Usuarios { get; }
    DbSet<Rol> Roles { get; }
    DbSet<Permiso> Permisos { get; }
    DbSet<RolPermiso> RolesPermisos { get; }
    DbSet<SesionUsuario> SesionesUsuario { get; }
    DbSet<Auditoria> Auditoria { get; }
    DbSet<ConfiguracionSistema> ConfiguracionSistema { get; }
    DbSet<Empleado> Empleados { get; }
    DbSet<Acudiente> Acudientes { get; }
    DbSet<Estudiante> Estudiantes { get; }
    DbSet<EstudianteAcudiente> EstudiantesAcudientes { get; }
    DbSet<Colegio> Colegios { get; }
    DbSet<ColegioCurso> ColegiosCursos { get; }
    DbSet<Vehiculo> Vehiculos { get; }
    DbSet<DocumentoVehiculo> DocumentosVehiculo { get; }
    DbSet<Ruta> Rutas { get; }
    DbSet<RutaEstudiante> RutasEstudiantes { get; }
    DbSet<AsignacionRuta> AsignacionesRuta { get; }
    DbSet<ViajeRuta> ViajesRuta { get; }
    DbSet<ParadaViaje> ParadasViaje { get; }
    DbSet<Evento> Eventos { get; }
    DbSet<GpsVehiculo> GpsVehiculos { get; }
    DbSet<Factura> Facturas { get; }
    DbSet<FacturaDetalle> FacturasDetalle { get; }
    DbSet<Pago> Pagos { get; }
    DbSet<Cotizacion> Cotizaciones { get; }
    DbSet<CotizacionEstudiante> CotizacionesEstudiantes { get; }
    DbSet<Gasto> Gastos { get; }
    DbSet<ProgramacionCobro> ProgramacionesCobro { get; }
    DbSet<PlantillaMensaje> PlantillasMensaje { get; }
    DbSet<EnvioProgramacion> EnviosProgramacion { get; }
    DbSet<Notificacion> Notificaciones { get; }
    DbSet<MensajeWhatsapp> MensajesWhatsapp { get; }
    DbSet<Archivo> Archivos { get; }

    /// <summary>Empresa del token (o <see cref="Guid.Empty"/>).</summary>
    Guid IdEmpresaActual { get; }

    /// <summary>
    /// Desactiva el filtro y la protección por empresa en esta petición. SOLO para operaciones de
    /// plataforma (SuperAdmin) y procesos del servidor; nunca con datos que vengan del body sin validar.
    /// </summary>
    void UsarModoSistema();

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public static class FiltrosConsulta
{
    /// <summary>Nombre del query filter por empresa (para <c>IgnoreQueryFilters([FiltrosConsulta.Empresa])</c>).</summary>
    public const string Empresa = "Empresa";
}
