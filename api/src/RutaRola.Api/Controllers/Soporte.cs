using Microsoft.AspNetCore.Mvc;
using RutaRola.Api.Seguridad;
using RutaRola.Application.Comun;
using RutaRola.Application.Soporte;
using RutaRola.Domain.Seguridad;

namespace RutaRola.Api.Controllers;

/// <summary>
/// Solicitudes de soporte de la empresa del token (administrador, <c>soporte.solicitar</c>). Solo ve las de
/// su empresa y nunca las notas internas de la plataforma.
/// </summary>
[Route("api/v1/soporte/tickets")]
[RequierePermiso(Permisos.SoporteSolicitar)]
public class SoporteController(ServicioSoporte servicio) : ControladorApi
{
    [HttpGet]
    public Task<ListaPaginada<TicketResumenDto>> Listar([FromQuery] FiltrosTicket filtros, [FromQuery] Paginacion paginacion, CancellationToken ct) =>
        servicio.ListarAsync(filtros, paginacion, plataforma: false, ct);

    [HttpGet("{id:guid}")]
    public Task<TicketDto> Obtener(Guid id, CancellationToken ct) => servicio.ObtenerAsync(id, plataforma: false, ct);

    /// <summary>Crea el ticket (consecutivo <c>SOP-0001</c>), avisa al equipo y confirma al solicitante por correo.</summary>
    [HttpPost]
    [ProducesResponseType<TicketDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Crear(SolicitudTicket solicitud, CancellationToken ct)
    {
        var ticket = await servicio.CrearAsync(solicitud, ct);
        return CreatedAtAction(nameof(Obtener), new { id = ticket.Id }, ticket);
    }

    [HttpPost("{id:guid}/comentarios")]
    public Task<TicketDto> Comentar(Guid id, SolicitudComentarioTicket solicitud, CancellationToken ct) =>
        servicio.ComentarAsync(id, solicitud with { Interno = false }, plataforma: false, ct);

    /// <summary>En VALIDACION_CLIENTE: aprobada → RESUELTO; si no, vuelve a EN_PROCESO (con comentario).</summary>
    [HttpPost("{id:guid}/validacion")]
    public Task<TicketDto> Validar(Guid id, SolicitudValidacionTicket solicitud, CancellationToken ct) => servicio.ValidarAsync(id, solicitud, ct);

    /// <summary>En RESUELTO: la empresa confirma con una calificación de 1 a 5 → CERRADO.</summary>
    [HttpPost("{id:guid}/confirmacion")]
    public Task<TicketDto> Confirmar(Guid id, SolicitudConfirmacionTicket solicitud, CancellationToken ct) => servicio.ConfirmarAsync(id, solicitud, ct);

    [HttpPost("{id:guid}/reapertura")]
    public Task<TicketDto> Reabrir(Guid id, SolicitudMotivoTicket solicitud, CancellationToken ct) => servicio.ReabrirAsync(id, solicitud, ct);

    [HttpPost("{id:guid}/cancelacion")]
    public Task<TicketDto> Cancelar(Guid id, SolicitudMotivoTicket solicitud, CancellationToken ct) => servicio.CancelarAsync(id, solicitud, ct);

    [HttpGet("{id:guid}/adjuntos/{idAdjunto:guid}")]
    [Produces("application/octet-stream")]
    public async Task<IActionResult> Adjunto(Guid id, Guid idAdjunto, CancellationToken ct)
    {
        var (nombre, tipo, contenido) = await servicio.AdjuntoAsync(id, idAdjunto, plataforma: false, ct);
        return File(contenido, tipo, nombre);
    }
}

/// <summary>Bandeja de tickets de TODAS las empresas (SuperAdmin, <c>soporte.gestionar</c>).</summary>
[Route("api/v1/plataforma/tickets")]
[RequierePermiso(Permisos.SoporteGestionar)]
public class PlataformaTicketsController(ServicioSoporte servicio) : ControladorApi
{
    [HttpGet]
    public Task<ListaPaginada<TicketResumenDto>> Listar([FromQuery] FiltrosTicket filtros, [FromQuery] Paginacion paginacion, CancellationToken ct) =>
        servicio.ListarAsync(filtros, paginacion, plataforma: true, ct);

    [HttpGet("resumen")]
    public Task<ResumenTicketsDto> Resumen(CancellationToken ct) => servicio.ResumenAsync(ct);

    /// <summary>Usuarios de la plataforma a los que se puede asignar un ticket.</summary>
    [HttpGet("responsables")]
    public Task<IReadOnlyList<ResponsableDto>> Responsables(CancellationToken ct) => servicio.ResponsablesAsync(ct);

    [HttpGet("{id:guid}")]
    public Task<TicketDto> Obtener(Guid id, CancellationToken ct) => servicio.ObtenerAsync(id, plataforma: true, ct);

    /// <summary>Avanza o devuelve el ticket según el flujo (422 si no se permite o falta el comentario).</summary>
    [HttpPost("{id:guid}/estado")]
    public Task<TicketDto> CambiarEstado(Guid id, SolicitudEstadoTicket solicitud, CancellationToken ct) => servicio.CambiarEstadoAsync(id, solicitud, ct);

    /// <summary>Comentario para la empresa o nota interna (<c>interno: true</c>, solo la ve la plataforma).</summary>
    [HttpPost("{id:guid}/comentarios")]
    public Task<TicketDto> Comentar(Guid id, SolicitudComentarioTicket solicitud, CancellationToken ct) =>
        servicio.ComentarAsync(id, solicitud, plataforma: true, ct);

    [HttpPut("{id:guid}/asignacion")]
    public Task<TicketDto> Asignar(Guid id, SolicitudAsignacionTicket solicitud, CancellationToken ct) => servicio.AsignarAsync(id, solicitud, ct);

    [HttpPost("{id:guid}/reuniones")]
    public Task<TicketDto> ProgramarReunion(Guid id, SolicitudReunionTicket solicitud, CancellationToken ct) =>
        servicio.ProgramarReunionAsync(id, solicitud, ct);

    /// <summary>Registra la reunión como REALIZADA (acta) o CANCELADA (motivo).</summary>
    [HttpPut("{id:guid}/reuniones/{idReunion:guid}")]
    public Task<TicketDto> ActualizarReunion(Guid id, Guid idReunion, SolicitudEstadoReunion solicitud, CancellationToken ct) =>
        servicio.ActualizarReunionAsync(id, idReunion, solicitud, ct);

    [HttpGet("{id:guid}/adjuntos/{idAdjunto:guid}")]
    [Produces("application/octet-stream")]
    public async Task<IActionResult> Adjunto(Guid id, Guid idAdjunto, CancellationToken ct)
    {
        var (nombre, tipo, contenido) = await servicio.AdjuntoAsync(id, idAdjunto, plataforma: true, ct);
        return File(contenido, tipo, nombre);
    }
}
