using Microsoft.AspNetCore.Mvc;
using RutaRola.Api.Seguridad;
using RutaRola.Application.Configuracion;
using RutaRola.Domain.Seguridad;

namespace RutaRola.Api.Controllers;

/// <summary>Parámetros de la empresa del token (día límite de pago, días de alerta…).</summary>
[RequierePermiso(Permisos.ConfiguracionGestionar)]
public class ConfiguracionController(ServicioConfiguracion servicio) : ControladorApi
{
    [HttpGet]
    public Task<IReadOnlyList<ParametroDto>> Listar(CancellationToken ct) => servicio.ListarAsync(ct);

    [HttpPut("{clave}")]
    public Task<ParametroDto> Actualizar(string clave, SolicitudParametro solicitud, CancellationToken ct) =>
        servicio.ActualizarAsync(clave, solicitud, ct);
}

/// <summary>
/// Formato de los PDF (facturas, recibos, cotizaciones) de la empresa del token. Lo lee cualquier usuario de la empresa
/// que genera documentos; solo el administrador lo cambia.
/// </summary>
[Route("api/v1/configuracion/documentos")]
public class FormatoDocumentosController(ServicioFormatoDocumentos servicio) : ControladorApi
{
    [HttpGet]
    public Task<FormatoDocumentosDto> Obtener(CancellationToken ct) => servicio.ObtenerAsync(ct);

    [HttpPut]
    [RequierePermiso(Permisos.ConfiguracionGestionar)]
    public Task<FormatoDocumentosDto> Actualizar(SolicitudFormatoDocumentos solicitud, CancellationToken ct) => servicio.ActualizarAsync(solicitud, ct);
}
