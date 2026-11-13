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

    /// <summary>Fotos de los estudiantes del recorrido (mismo acceso que el recorrido).</summary>
    [HttpGet("{id:guid}/fotos")]
    public async Task<ActionResult<IReadOnlyList<FotoEstudianteDto>>> Fotos(Guid id, CancellationToken ct)
    {
        if ((await autorizacion.AuthorizeAsync(User, Permisos.RecorridosLeer)).Succeeded)
        {
            return Ok(await servicio.FotosAsync(id, true, ct));
        }
        if ((await autorizacion.AuthorizeAsync(User, Permisos.RecorridosOperar)).Succeeded)
        {
            return Ok(await servicio.FotosAsync(id, false, ct));
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

    // ---------- GPS (fase 5) ----------

    /// <summary>
    /// Lote de lecturas del teléfono del conductor o la monitora (máximo 500). Las repetidas o fuera del
    /// periodo del recorrido se descartan; se aceptan después de finalizar las tomadas antes del fin (sin señal).
    /// </summary>
    [HttpPost("{id:guid}/gps")]
    [RequierePermiso(Permisos.GpsReportar)]
    public Task<ResultadoLoteGps> RegistrarGps(Guid id, SolicitudLoteGps solicitud, [FromServices] ServicioGps gps, CancellationToken ct) =>
        gps.RegistrarAsync(id, solicitud, ct);

    /// <summary>Última posición (204 si aún no hay). El acudiente solo la ve mientras el recorrido de su estudiante está en curso.</summary>
    [HttpGet("{id:guid}/gps/ultima")]
    [RequiereFuncionPlan(Application.Suscripciones.FuncionPlan.GpsEnVivo)]
    [ProducesResponseType<PosicionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<PosicionDto?>> UltimaPosicion(Guid id, [FromServices] ServicioGps gps, CancellationToken ct)
    {
        if (await AlcanceGpsDe.ResolverAsync(autorizacion, User) is not { } alcance)
        {
            return Forbid();
        }
        return await gps.UltimaAsync(id, alcance, ct) is { } posicion ? posicion : NoContent();
    }

    /// <summary>Trayecto en orden (administrador o personal del recorrido); <c>desde</c> trae solo lo nuevo.</summary>
    [HttpGet("{id:guid}/gps")]
    [RequiereFuncionPlan(Application.Suscripciones.FuncionPlan.GpsEnVivo)]
    public async Task<ActionResult<IReadOnlyList<PosicionDto>>> Trayecto(Guid id, [FromQuery] DateTime? desde, [FromServices] ServicioGps gps, CancellationToken ct)
    {
        if (await AlcanceGpsDe.ResolverAsync(autorizacion, User) is not { } alcance || alcance == AlcanceGps.Acudiente)
        {
            return Forbid();
        }
        return Ok(await gps.TrayectoAsync(id, alcance, desde, ct));
    }

    /// <summary>Recorridos en curso de la empresa con su última posición (mapa del administrador).</summary>
    [HttpGet("en-curso")]
    [RequiereFuncionPlan(Application.Suscripciones.FuncionPlan.GpsEnVivo)]
    [RequierePermiso(Permisos.RecorridosLeer)]
    public Task<IReadOnlyList<PosicionRecorridoDto>> EnCurso([FromServices] ServicioGps gps, CancellationToken ct) => gps.EnCursoAsync(ct);

    [HttpPost("{id:guid}/finalizar")]
    [RequierePermiso(Permisos.RecorridosOperar)]
    public Task<RecorridoDto> Finalizar(Guid id, SolicitudFinalizarRecorrido solicitud, CancellationToken ct) =>
        servicio.FinalizarAsync(id, solicitud, ct);
}

/// <summary>Avisos de ausencia de los acudientes (Operación › Asignaciones): <c>?fecha=YYYY-MM-DD&amp;rutaId=</c>, hoy por defecto.</summary>
[Route("api/v1/avisos-ausencia")]
[RequierePermiso(Permisos.RecorridosLeer)]
public class AvisosAusenciaController(ServicioAvisosAusencia servicio) : ControladorApi
{
    [HttpGet]
    public Task<IReadOnlyList<AvisoAusenciaDto>> Listar([FromQuery] FiltrosAvisoAusencia filtros, CancellationToken ct) => servicio.ListarAsync(filtros, ct);
}
