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
public class EmpresasController(
    ServicioEmpresas servicio, ServicioAdministradoresEmpresa administradores, RutaRola.Application.Suscripciones.ServicioSuscripcion suscripciones) : ControladorApi
{
    /// <summary>Plan de una empresa (plataforma).</summary>
    [HttpGet("{id:guid}/suscripcion")]
    [RequierePermiso(Permisos.EmpresasGestionar)]
    public Task<RutaRola.Application.Suscripciones.SuscripcionDto> Suscripcion(Guid id, CancellationToken ct) => suscripciones.DeEmpresaAsync(id, ct);

    /// <summary>Ajuste manual del plan: convenio, pago por transferencia o extensión de la prueba.</summary>
    [HttpPut("{id:guid}/suscripcion")]
    [RequierePermiso(Permisos.EmpresasGestionar)]
    public Task<RutaRola.Application.Suscripciones.SuscripcionDto> AjustarSuscripcion(
        Guid id, RutaRola.Application.Suscripciones.SolicitudAjusteSuscripcion solicitud, CancellationToken ct) =>
        suscripciones.AjustarAsync(id, solicitud, ct);

    [HttpGet("{id:guid}/suscripcion/pagos")]
    [RequierePermiso(Permisos.EmpresasGestionar)]
    public Task<IReadOnlyList<RutaRola.Application.Suscripciones.PagoSuscripcionDto>> PagosSuscripcion(Guid id, CancellationToken ct) =>
        suscripciones.PagosDeEmpresaAsync(id, ct);

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

    /// <summary>Administradores de la empresa (sin contraseña).</summary>
    [HttpGet("{id:guid}/administradores")]
    [RequierePermiso(Permisos.EmpresasGestionar)]
    public Task<IReadOnlyList<AdministradorEmpresaDto>> ListarAdministradores(Guid id, CancellationToken ct) =>
        administradores.ListarAsync(id, ct);

    [HttpPost("{id:guid}/administradores")]
    [RequierePermiso(Permisos.EmpresasGestionar)]
    [ProducesResponseType<AdministradorEmpresaDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CrearAdministrador(Guid id, SolicitudAdministradorEmpresa solicitud, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await administradores.CrearAsync(id, solicitud, ct));

    /// <summary>Contraseña vacía = se conserva. Cambiarla o inactivar cierra sus sesiones.</summary>
    [HttpPut("{id:guid}/administradores/{idUsuario:guid}")]
    [RequierePermiso(Permisos.EmpresasGestionar)]
    public Task<AdministradorEmpresaDto> ActualizarAdministrador(Guid id, Guid idUsuario, SolicitudAdministradorEmpresa solicitud, CancellationToken ct) =>
        administradores.ActualizarAsync(id, idUsuario, solicitud, ct);

    /// <summary>Restablece la contraseña del administrador y cierra todas sus sesiones.</summary>
    [HttpPut("{id:guid}/administradores/{idUsuario:guid}/contrasena")]
    [RequierePermiso(Permisos.EmpresasGestionar)]
    public async Task<IActionResult> CambiarContrasenaAdministrador(Guid id, Guid idUsuario, SolicitudContrasenaAdministrador solicitud, CancellationToken ct)
    {
        await administradores.CambiarContrasenaAsync(id, idUsuario, solicitud, ct);
        return NoContent();
    }

    /// <summary>Empresa del usuario autenticado.</summary>
    [HttpGet("actual")]
    public Task<EmpresaDto> ObtenerActual(CancellationToken ct) => servicio.ObtenerActualAsync(ct);

    /// <summary>El administrador actualiza los datos de contacto de su empresa (no el NIT ni el estado).</summary>
    [HttpPut("actual")]
    [RequierePermiso(Permisos.ConfiguracionGestionar)]
    public Task<EmpresaDto> ActualizarActual(SolicitudEmpresaPropia solicitud, CancellationToken ct) =>
        servicio.ActualizarActualAsync(solicitud, ct);
}
