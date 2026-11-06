using Microsoft.AspNetCore.Mvc;
using RutaRola.Application.Comun;

namespace RutaRola.Api.Controllers;

/// <summary>
/// Contrato CRUD del frontend (<c>shared/crud/repositorio-http.ts</c>):
/// <list type="bullet">
/// <item><c>GET /x?filtros&amp;pagina&amp;tamano</c> → <c>{ items, total, pagina, tamano }</c></item>
/// <item><c>GET /x/opciones?filtros</c> → <c>[{ id, nombre }]</c></item>
/// <item><c>GET /x/{id}</c>, <c>POST /x</c> (201), <c>PUT /x/{id}</c>, <c>DELETE /x/{id}</c> (204; 409 con el motivo si está en uso)</item>
/// <item><c>GET /x/{id}/motivo-no-eliminar</c> → <c>{ motivo }</c> (null si se puede eliminar)</item>
/// </list>
/// Los permisos se declaran en cada controlador con <c>[PermisosCrud(leer, gestionar)]</c>.
/// </summary>
public abstract class ControladorCrud<TDto, TSolicitud, TFiltros>(IServicioCrud<TDto, TSolicitud, TFiltros> servicio) : ControladorApi
    where TFiltros : notnull
{
    [HttpGet]
    public Task<ListaPaginada<TDto>> Listar([FromQuery] TFiltros filtros, [FromQuery] Paginacion paginacion, CancellationToken ct) =>
        servicio.ListarAsync(filtros, paginacion, ct);

    [HttpGet("opciones")]
    public Task<IReadOnlyList<Opcion>> Opciones([FromQuery] TFiltros filtros, CancellationToken ct) => servicio.OpcionesAsync(filtros, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public Task<TDto> Obtener(Guid id, CancellationToken ct) => servicio.ObtenerAsync(id, ct);

    [HttpGet("{id:guid}/motivo-no-eliminar")]
    public async Task<RespuestaMotivo> MotivoNoEliminar(Guid id, CancellationToken ct)
    {
        await servicio.ObtenerAsync(id, ct); // 404 si no existe (o es de otra empresa).
        return new RespuestaMotivo(await servicio.MotivoNoEliminarAsync(id, ct));
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<TDto>> Crear(TSolicitud solicitud, CancellationToken ct)
    {
        var creado = await servicio.CrearAsync(solicitud, ct);
        var id = typeof(TDto).GetProperty("Id")?.GetValue(creado);
        return CreatedAtAction(nameof(Obtener), new { id }, creado);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public Task<TDto> Actualizar(Guid id, TSolicitud solicitud, CancellationToken ct) => servicio.ActualizarAsync(id, solicitud, ct);

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Eliminar(Guid id, CancellationToken ct)
    {
        await servicio.EliminarAsync(id, ct);
        return NoContent();
    }
}
