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
