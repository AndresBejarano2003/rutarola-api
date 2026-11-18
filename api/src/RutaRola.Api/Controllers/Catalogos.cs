using Microsoft.AspNetCore.Mvc;
using RutaRola.Api.Seguridad;
using RutaRola.Application.Comun;
using RutaRola.Application.Operacion;
using RutaRola.Application.Personas;
using RutaRola.Domain.Seguridad;

namespace RutaRola.Api.Controllers;

/// <summary>Colegios y sus cursos (<c>operacion/colegios/colegios-http.ts</c>).</summary>
[PermisosCrud(Permisos.ColegiosLeer, Permisos.ColegiosGestionar)]
public class ColegiosController(ServicioColegios servicio)
    : ControladorCrud<ColegioDto, SolicitudColegio, FiltrosColegio>(servicio);

/// <summary>Vehículos con sus documentos (<c>operacion/vehiculos/vehiculos-http.ts</c>).</summary>
[PermisosCrud(Permisos.VehiculosLeer, Permisos.VehiculosGestionar)]
public class VehiculosController(ServicioVehiculos servicio)
    : ControladorCrud<VehiculoDto, SolicitudVehiculo, FiltrosVehiculo>(servicio);

/// <summary>Historial de documentos del vehículo con su PDF (<c>operacion/vehiculos/documentos-vehiculo-http.ts</c>).</summary>
[Route("api/v1/vehiculos/{idVehiculo:guid}/documentos")]
[PermisosCrud(Permisos.VehiculosLeer, Permisos.VehiculosGestionar)]
public class DocumentosVehiculoController(ServicioDocumentosVehiculo servicio) : ControladorApi
{
    [HttpGet]
    public Task<IReadOnlyList<DocumentoHistorialDto>> Listar(Guid idVehiculo, CancellationToken ct) => servicio.ListarAsync(idVehiculo, ct);

    [HttpGet("{id:guid}")]
    public Task<DocumentoHistorialDto> Obtener(Guid idVehiculo, Guid id, CancellationToken ct) => servicio.ObtenerAsync(idVehiculo, id, ct);

    [HttpPost]
    [ProducesResponseType<DocumentoHistorialDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Crear(Guid idVehiculo, SolicitudDocumentoHistorial solicitud, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await servicio.CrearAsync(idVehiculo, solicitud, ct));

    [HttpPut("{id:guid}")]
    public Task<DocumentoHistorialDto> Actualizar(Guid idVehiculo, Guid id, SolicitudDocumentoHistorial solicitud, CancellationToken ct) =>
        servicio.ActualizarAsync(idVehiculo, id, solicitud, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Eliminar(Guid idVehiculo, Guid id, CancellationToken ct)
    {
        await servicio.EliminarAsync(idVehiculo, id, ct);
        return NoContent();
    }
}

/// <summary>
/// Gastos de los vehículos (<c>operacion/vehiculos/gastos-vehiculo/gastos-vehiculo-http.ts</c>): CRUD +
/// <c>GET /gastos/resumen?vehiculoId=&amp;tipo=&amp;desde=&amp;hasta=</c> (tablero).
/// </summary>
[PermisosCrud(Permisos.GastosLeer, Permisos.GastosGestionar)]
public class GastosController(ServicioGastosVehiculo servicio)
    : ControladorCrud<GastoVehiculoDto, SolicitudGastoVehiculo, FiltrosGastoVehiculo>(servicio)
{
    [HttpGet("resumen")]
    public Task<ResumenGastosDto> Resumen([FromQuery] FiltrosGastoVehiculo filtros, CancellationToken ct) => servicio.ResumenAsync(filtros, ct);
}

/// <summary>
/// Novedades de los vehículos (<c>operacion/vehiculos/novedades-vehiculo/novedades-vehiculo-http.ts</c>). El
/// administrador lista, registra y atiende; el conductor/monitora usa <c>/novedades-vehiculo/mias</c>.
/// </summary>
[Route("api/v1/novedades-vehiculo")]
public class NovedadesVehiculoController(ServicioNovedadesVehiculo servicio) : ControladorApi
{
    [HttpGet]
    [RequierePermiso(Permisos.VehiculosLeer)]
    public Task<IReadOnlyList<NovedadVehiculoDto>> Listar([FromQuery] FiltrosNovedadVehiculo filtros, CancellationToken ct) =>
        servicio.ListarAsync(filtros, ct);

    [HttpGet("{id:guid}")]
    [RequierePermiso(Permisos.VehiculosLeer)]
    public Task<NovedadVehiculoDto> Obtener(Guid id, CancellationToken ct) => servicio.ObtenerAsync(id, ct);

    [HttpPost]
    [RequierePermiso(Permisos.VehiculosGestionar)]
    [ProducesResponseType<NovedadVehiculoDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Registrar(SolicitudNovedadVehiculo solicitud, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await servicio.RegistrarAsync(solicitud, ct));

    /// <summary>PENDIENTE / EN_PROCESO / RESUELTA (con respuesta).</summary>
    [HttpPut("{id:guid}/estado")]
    [RequierePermiso(Permisos.VehiculosGestionar)]
    public Task<NovedadVehiculoDto> CambiarEstado(Guid id, SolicitudEstadoNovedad solicitud, CancellationToken ct) =>
        servicio.CambiarEstadoAsync(id, solicitud, ct);

    /// <summary>Vehículos de las asignaciones vigentes o próximas del empleado del token.</summary>
    [HttpGet("mis-vehiculos")]
    [RequierePermiso(Permisos.NovedadesReportar)]
    public Task<IReadOnlyList<Opcion>> MisVehiculos(CancellationToken ct) => servicio.MisVehiculosAsync(ct);

    /// <summary>Las de sus vehículos (y las que reportó), abiertas primero.</summary>
    [HttpGet("mias")]
    [RequierePermiso(Permisos.NovedadesReportar)]
    public Task<IReadOnlyList<NovedadVehiculoDto>> Mias(CancellationToken ct) => servicio.MiasAsync(ct);

    [HttpGet("mias/{id:guid}")]
    [RequierePermiso(Permisos.NovedadesReportar)]
    public Task<NovedadVehiculoDto> ObtenerMia(Guid id, CancellationToken ct) => servicio.ObtenerMiaAsync(id, ct);

    [HttpPost("mias")]
    [RequierePermiso(Permisos.NovedadesReportar)]
    [ProducesResponseType<NovedadVehiculoDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Reportar(SolicitudNovedadVehiculo solicitud, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await servicio.ReportarAsync(solicitud, ct));
}

/// <summary>Conductores y monitoras con su acceso a la Ruta del día (<c>personas/empleados/empleados-http.ts</c>).</summary>
[PermisosCrud(Permisos.EmpleadosLeer, Permisos.EmpleadosGestionar)]
public class EmpleadosController(ServicioEmpleados servicio)
    : ControladorCrud<EmpleadoDto, SolicitudEmpleado, FiltrosEmpleado>(servicio);

/// <summary>Acudientes con su acceso al portal (<c>personas/acudientes/acudientes-http.ts</c>).</summary>
[PermisosCrud(Permisos.AcudientesLeer, Permisos.AcudientesGestionar)]
public class AcudientesController(ServicioAcudientes servicio)
    : ControladorCrud<AcudienteDto, SolicitudAcudiente, FiltrosAcudiente>(servicio);

/// <summary>Estudiantes (datos de menores) (<c>personas/estudiantes/estudiantes-http.ts</c>).</summary>
[PermisosCrud(Permisos.EstudiantesLeer, Permisos.EstudiantesGestionar)]
public class EstudiantesController(ServicioEstudiantes servicio)
    : ControladorCrud<EstudianteDto, SolicitudEstudiante, FiltrosEstudiante>(servicio);

/// <summary>Vista de solo lectura de los conductores con su licencia y asignación activa.</summary>
public class ConductoresController(ServicioConductores servicio) : ControladorApi
{
    [HttpGet]
    [RequierePermiso(Permisos.EmpleadosLeer)]
    public Task<ListaPaginada<ConductorDto>> Listar([FromQuery] FiltrosConductor filtros, [FromQuery] Paginacion paginacion, CancellationToken ct) =>
        servicio.ListarAsync(filtros, paginacion, ct);
}
