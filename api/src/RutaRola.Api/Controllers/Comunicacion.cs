using Microsoft.AspNetCore.Mvc;
using RutaRola.Api.Seguridad;
using RutaRola.Application.Comun;
using RutaRola.Application.Comunicacion;
using RutaRola.Domain.Seguridad;

namespace RutaRola.Api.Controllers;

/// <summary>Bandeja del usuario autenticado (<c>inicio/notificaciones/notificaciones-http.ts</c>). Las crea el servidor.</summary>
[RequierePermiso(Permisos.NotificacionesPropias)]
public class NotificacionesController(ServicioNotificaciones servicio) : ControladorApi
{
    [HttpGet]
    public Task<ListaPaginada<NotificacionDto>> Listar([FromQuery] FiltrosNotificacion filtros, [FromQuery] Paginacion paginacion, CancellationToken ct) =>
        servicio.ListarAsync(filtros, paginacion, ct);

    [HttpGet("resumen")]
    public Task<ResumenNotificacionesDto> Resumen(CancellationToken ct) => servicio.ResumenAsync(ct);

    [HttpPatch("lectura")]
    public async Task<IActionResult> MarcarLeidas(SolicitudLectura solicitud, CancellationToken ct)
    {
        await servicio.MarcarLeidasAsync(solicitud, ct);
        return NoContent();
    }

    [HttpPost("leer-todas")]
    public async Task<IActionResult> LeerTodas(CancellationToken ct)
    {
        await servicio.MarcarTodasLeidasAsync(ct);
        return NoContent();
    }

    [HttpPatch("archivo")]
    public async Task<IActionResult> Archivar(SolicitudArchivo solicitud, CancellationToken ct)
    {
        await servicio.ArchivarAsync(solicitud, ct);
        return NoContent();
    }

    [HttpPost("eliminar")]
    public async Task<IActionResult> Eliminar(SolicitudIds solicitud, CancellationToken ct)
    {
        await servicio.EliminarAsync(solicitud, ct);
        return NoContent();
    }

    /// <summary><c>bandeja</c> ENTRADA | ARCHIVADAS: elimina todas las de esa bandeja.</summary>
    [HttpDelete]
    public async Task<IActionResult> Vaciar([FromQuery] string? bandeja, CancellationToken ct)
    {
        await servicio.VaciarAsync(bandeja, ct);
        return NoContent();
    }
}

/// <summary>Portal del acudiente del token (<c>portal/portal-http.ts</c>), solo lectura.</summary>
[RequierePermiso(Permisos.PortalConsultar)]
public class PortalController(ServicioPortal servicio) : ControladorApi
{
    [HttpGet]
    public Task<PortalAcudienteDto> Obtener(CancellationToken ct) => servicio.ObtenerAsync(ct);
}

/// <summary>Registro de los mensajes de WhatsApp que envió el servidor (solo lectura).</summary>
[Route("api/v1/mensajes-whatsapp")]
[RequierePermiso(Permisos.MensajesLeer)]
public class MensajesWhatsappController(ServicioMensajesWhatsapp servicio) : ControladorApi
{
    [HttpGet]
    public Task<ListaPaginada<MensajeWhatsappDto>> Listar(
        [FromQuery] FiltrosMensajeWhatsapp filtros, [FromQuery] Paginacion paginacion, CancellationToken ct) =>
        servicio.ListarAsync(filtros, paginacion, ct);
}
