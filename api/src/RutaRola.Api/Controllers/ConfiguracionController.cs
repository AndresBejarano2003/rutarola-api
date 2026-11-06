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
