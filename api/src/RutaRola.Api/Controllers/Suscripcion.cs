using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RutaRola.Api.Seguridad;
using RutaRola.Application.Suscripciones;
using RutaRola.Domain.Seguridad;

namespace RutaRola.Api.Controllers;

/// <summary>
/// Plan de la empresa del token y sus pagos en línea (Wompi Web Checkout). <c>GET /suscripcion/planes</c> es
/// público (precios). El plan solo se activa cuando Wompi confirma el pago (evento firmado o consulta de la
/// transacción); la redirección del navegador nunca activa nada.
/// </summary>
[Route("api/v1/suscripcion")]
public class SuscripcionController(ServicioSuscripcion servicio) : ControladorApi
{
    [HttpGet("planes")]
    [AllowAnonymous]
    public IReadOnlyList<PlanDto> Planes() => ServicioSuscripcion.Planes();

    [HttpGet]
    [RequierePermiso(Permisos.SuscripcionLeer)]
    public Task<SuscripcionDto> Obtener(CancellationToken ct) => servicio.ObtenerAsync(ct);

    [HttpGet("pagos")]
    [RequierePermiso(Permisos.SuscripcionLeer)]
    public Task<IReadOnlyList<PagoSuscripcionDto>> Pagos(CancellationToken ct) => servicio.PagosAsync(ct);

    /// <summary>Crea el pago PENDIENTE y devuelve la URL firmada del checkout de Wompi (422 si Wompi no está configurado).</summary>
    [HttpPost("pagos")]
    [RequierePermiso(Permisos.SuscripcionGestionar)]
    [ProducesResponseType<CheckoutDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> IniciarPago(SolicitudPagoSuscripcion solicitud, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await servicio.IniciarPagoAsync(solicitud, ct));

    /// <summary>Al volver de Wompi: consulta la transacción (<c>?id=</c> de la redirección) en Wompi y la aplica.</summary>
    [HttpPost("pagos/{referencia}/verificacion")]
    [RequierePermiso(Permisos.SuscripcionGestionar)]
    public Task<PagoSuscripcionDto> Verificar(string referencia, SolicitudVerificacionPago solicitud, CancellationToken ct) =>
        servicio.VerificarPagoAsync(referencia, solicitud, ct);
}

/// <summary>
/// URL de eventos que se registra en el panel de Wompi (Desarrolladores › URL de eventos):
/// <c>https://&lt;api&gt;/api/v1/pagos/wompi/eventos</c>. Anónima: se verifica la firma con el secreto de eventos.
/// 200 = recibido (Wompi reintenta si no recibe 200); 401 = firma inválida.
/// </summary>
[Route("api/v1/pagos/wompi")]
public class EventosWompiController(ServicioSuscripcion servicio) : ControladorApi
{
    [HttpPost("eventos")]
    [AllowAnonymous]
    public async Task<IActionResult> Evento([FromBody] JsonElement evento, CancellationToken ct) =>
        await servicio.ProcesarEventoAsync(evento, ct) ? Ok() : Unauthorized();
}
