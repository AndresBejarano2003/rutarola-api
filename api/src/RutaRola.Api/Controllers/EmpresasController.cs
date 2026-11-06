using Microsoft.AspNetCore.Mvc;
using RutaRola.Api.Seguridad;
using RutaRola.Application.Comun;
using RutaRola.Application.Empresas;
using RutaRola.Domain.Seguridad;

namespace RutaRola.Api.Controllers;

/// <summary>
/// Empresas de transporte. Listar, crear y editar cualquiera es de la plataforma (SuperAdmin);
/// <c>/empresas/actual</c> es la empresa del usuario autenticado.
/// </summary>
public class EmpresasController(ServicioEmpresas servicio) : ControladorApi
{
    [HttpGet]
    [RequierePermiso(Permisos.EmpresasGestionar)]
    public Task<ListaPaginada<EmpresaDto>> Listar([FromQuery] FiltrosEmpresa filtros, [FromQuery] Paginacion paginacion, CancellationToken ct) =>
        servicio.ListarAsync(filtros, paginacion, ct);

    [HttpGet("{id:guid}")]
    [RequierePermiso(Permisos.EmpresasGestionar)]
    public Task<EmpresaDto> Obtener(Guid id, CancellationToken ct) => servicio.ObtenerAsync(id, ct);

    /// <summary>Crea la empresa con su configuración por defecto y, opcionalmente, su primer administrador.</summary>
    [HttpPost]
    [RequierePermiso(Permisos.EmpresasGestionar)]
    [ProducesResponseType<EmpresaDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Crear(SolicitudEmpresa solicitud, CancellationToken ct)
    {
        var empresa = await servicio.CrearAsync(solicitud, ct);
        return CreatedAtAction(nameof(Obtener), new { id = empresa.Id }, empresa);
    }

    [HttpPut("{id:guid}")]
    [RequierePermiso(Permisos.EmpresasGestionar)]
    public Task<EmpresaDto> Actualizar(Guid id, SolicitudEmpresa solicitud, CancellationToken ct) =>
        servicio.ActualizarAsync(id, solicitud, ct);

    /// <summary>Empresa del usuario autenticado.</summary>
    [HttpGet("actual")]
    public Task<EmpresaDto> ObtenerActual(CancellationToken ct) => servicio.ObtenerActualAsync(ct);

    /// <summary>El administrador actualiza los datos de contacto de su empresa (no el NIT ni el estado).</summary>
    [HttpPut("actual")]
    [RequierePermiso(Permisos.ConfiguracionGestionar)]
    public Task<EmpresaDto> ActualizarActual(SolicitudEmpresaPropia solicitud, CancellationToken ct) =>
        servicio.ActualizarActualAsync(solicitud, ct);
}
