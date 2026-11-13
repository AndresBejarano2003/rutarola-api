using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RutaRola.Api.Seguridad;
using RutaRola.Application.Comun;
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

    /// <summary>Días de prueba que recibe hoy una empresa nueva (registro y precios públicos).</summary>
    [HttpGet("condiciones")]
    [AllowAnonymous]
    public Task<CondicionesRegistroDto> Condiciones([FromServices] ServicioParametrosPlataforma parametros, CancellationToken ct) =>
        parametros.CondicionesAsync(ct);

    /// <summary>La prueba o el plan venció y el administrador no quiere pagar: sigue en Gratis e inactiva lo que sobre.</summary>
    [HttpPost("continuar-gratis")]
    [RequierePermiso(Permisos.SuscripcionGestionar)]
    public Task<DecisionGratisDto> ContinuarGratis(CancellationToken ct) => servicio.ContinuarGratisAsync(ct);

    [HttpGet]
    [RequierePermiso(Permisos.SuscripcionLeer)]
    public Task<SuscripcionDto> Obtener(CancellationToken ct) => servicio.ObtenerAsync(ct);

    [HttpGet("pagos")]
    [RequierePermiso(Permisos.SuscripcionLeer)]
    public Task<IReadOnlyList<PagoSuscripcionDto>> Pagos(CancellationToken ct) => servicio.PagosAsync(ct);

    /// <summary>Valida un código promocional para el plan y periodo (422 con el motivo si no aplica).</summary>
    [HttpPost("codigo")]
    [RequierePermiso(Permisos.SuscripcionGestionar)]
    public Task<ValidacionCodigoDto> ValidarCodigo(SolicitudValidacionCodigo solicitud, CancellationToken ct) =>
        servicio.ValidarCodigoAsync(solicitud, ct);

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

/// <summary>
/// Códigos promocionales de la plataforma (SuperAdmin). Descuentan solo el primer mes facturado. Un código con usos
/// no se elimina (409): se inactiva.
/// </summary>
[Route("api/v1/plataforma/codigos-promocionales")]
[RequierePermiso(Permisos.PromocionesGestionar)]
public class CodigosPromocionalesController(ServicioCodigosPromocionales servicio) : ControladorApi
{
    [HttpGet]
    public Task<ListaPaginada<CodigoPromocionalDto>> Listar(
        [FromQuery] FiltrosCodigoPromocional filtros, [FromQuery] Paginacion paginacion, CancellationToken ct) =>
        servicio.ListarAsync(filtros, paginacion, ct);

    [HttpGet("opciones")]
    public Task<IReadOnlyList<Opcion>> Opciones(CancellationToken ct) => servicio.OpcionesAsync(ct);

    [HttpGet("{id:guid}")]
    public Task<CodigoPromocionalDto> Obtener(Guid id, CancellationToken ct) => servicio.ObtenerAsync(id, ct);

    [HttpPost]
    [ProducesResponseType<CodigoPromocionalDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Crear(SolicitudCodigoPromocional solicitud, CancellationToken ct)
    {
        var codigo = await servicio.CrearAsync(solicitud, ct);
        return CreatedAtAction(nameof(Obtener), new { id = codigo.Id }, codigo);
    }

    [HttpPut("{id:guid}")]
    public Task<CodigoPromocionalDto> Actualizar(Guid id, SolicitudCodigoPromocional solicitud, CancellationToken ct) =>
        servicio.ActualizarAsync(id, solicitud, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Eliminar(Guid id, CancellationToken ct)
    {
        await servicio.EliminarAsync(id, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/motivo-no-eliminar")]
    public async Task<RespuestaMotivo> MotivoNoEliminar(Guid id, CancellationToken ct) => new(await servicio.MotivoNoEliminarAsync(id, ct));

    [HttpGet("{id:guid}/usos")]
    public Task<IReadOnlyList<UsoCodigoDto>> Usos(Guid id, CancellationToken ct) => servicio.UsosAsync(id, ct);
}

/// <summary>Parámetros globales de la plataforma (SuperAdmin): días de la prueba gratuita.</summary>
[Route("api/v1/plataforma/parametros")]
[RequierePermiso(Permisos.EmpresasGestionar)]
public class ParametrosPlataformaController(ServicioParametrosPlataforma servicio) : ControladorApi
{
    [HttpGet]
    public Task<IReadOnlyList<ParametroPlataformaDto>> Listar(CancellationToken ct) => servicio.ListarAsync(ct);

    [HttpPut("{clave}")]
    public Task<ParametroPlataformaDto> Actualizar(string clave, SolicitudParametroPlataforma solicitud, CancellationToken ct) =>
        servicio.ActualizarAsync(clave, solicitud, ct);
}
