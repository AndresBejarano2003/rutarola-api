using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RutaRola.Api.Seguridad;
using RutaRola.Application.Comun;
using RutaRola.Application.Operacion;
using RutaRola.Domain.Seguridad;

namespace RutaRola.Api.Controllers;

/// <summary>Rutas y sus estudiantes en orden de recogida (<c>operacion/rutas/rutas-http.ts</c>).</summary>
[PermisosCrud(Permisos.RutasLeer, Permisos.RutasGestionar)]
public class RutasController(ServicioRutas servicio) : ControladorCrud<RutaDto, SolicitudRuta, FiltrosRuta>(servicio);

/// <summary>Vehículo + conductor (+ monitora) por ruta (<c>operacion/asignaciones/asignaciones-http.ts</c>).</summary>
[PermisosCrud(Permisos.AsignacionesLeer, Permisos.AsignacionesGestionar)]
public class AsignacionesController(ServicioAsignaciones servicio)
    : ControladorCrud<AsignacionDto, SolicitudAsignacion, FiltrosAsignacion>(servicio);

/// <summary>
/// Ruta del día del conductor/monitora (<c>ruta-del-dia/recorridos-http.ts</c>) e historial del
/// administrador (<c>operacion/recorridos/historial-recorridos-http.ts</c>). El empleado sale del token.
/// </summary>
public class RecorridosController(ServicioRecorridos servicio, IAuthorizationService autorizacion, TimeProvider reloj) : ControladorApi
{
    [HttpGet]
    [RequierePermiso(Permisos.RecorridosLeer)]
    public Task<ListaPaginada<RecorridoDto>> Listar([FromQuery] FiltrosRecorrido filtros, [FromQuery] Paginacion paginacion, CancellationToken ct) =>
        servicio.ListarAsync(filtros, paginacion, ct);

    /// <summary>Rutas que el empleado cubre en la fecha (hoy si no se indica) con sus recorridos.</summary>
    [HttpGet("del-dia")]
    [RequierePermiso(Permisos.RecorridosOperar)]
    public Task<IReadOnlyList<RutaAsignadaDto>> DelDia([FromQuery] DateOnly? fecha, CancellationToken ct) =>
        servicio.RutasDelDiaAsync(fecha ?? Fechas.Hoy(reloj), ct);

    /// <summary>El administrador ve cualquiera de su empresa; el conductor o la monitora, solo los suyos (403).</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<RecorridoDto>> Obtener(Guid id, CancellationToken ct)
    {
        if ((await autorizacion.AuthorizeAsync(User, Permisos.RecorridosLeer)).Succeeded)
        {
            return await servicio.ObtenerAsync(id, true, ct);
        }
        if ((await autorizacion.AuthorizeAsync(User, Permisos.RecorridosOperar)).Succeeded)
        {
            return await servicio.ObtenerAsync(id, false, ct);
        }
        return Forbid();
    }

    /// <summary>201 con el recorrido nuevo, o 200 con el que el compañero ya inició (se une).</summary>
    [HttpPost]
    [RequierePermiso(Permisos.RecorridosOperar)]
    [ProducesResponseType<RecorridoDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<RecorridoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<RecorridoDto>> Iniciar(SolicitudIniciarRecorrido solicitud, CancellationToken ct)
    {
        var (recorrido, nuevo) = await servicio.IniciarAsync(solicitud, ct);
        return nuevo ? CreatedAtAction(nameof(Obtener), new { id = recorrido.Id }, recorrido) : Ok(recorrido);
    }

    [HttpPatch("{id:guid}/paradas")]
    [RequierePermiso(Permisos.RecorridosOperar)]
    public Task<RecorridoDto> Marcar(Guid id, SolicitudMarcarParadas solicitud, CancellationToken ct) =>
        servicio.MarcarAsync(id, solicitud, ct);

    [HttpPut("{id:guid}/orden")]
    [RequierePermiso(Permisos.RecorridosOperar)]
    public Task<RecorridoDto> Reordenar(Guid id, SolicitudOrdenParadas solicitud, CancellationToken ct) =>
        servicio.ReordenarAsync(id, solicitud, ct);

    [HttpPost("{id:guid}/finalizar")]
    [RequierePermiso(Permisos.RecorridosOperar)]
    public Task<RecorridoDto> Finalizar(Guid id, SolicitudFinalizarRecorrido solicitud, CancellationToken ct) =>
        servicio.FinalizarAsync(id, solicitud, ct);
}
