using Microsoft.AspNetCore.Mvc;
using RutaRola.Api.Seguridad;
using RutaRola.Application.Finanzas;
using RutaRola.Domain.Seguridad;

namespace RutaRola.Api.Controllers;

/// <summary>Contratos mensuales (<c>finanzas/facturas/facturas-http.ts</c>).</summary>
[PermisosCrud(Permisos.FacturasLeer, Permisos.FacturasGestionar)]
public class FacturasController(ServicioFacturas servicio) : ControladorCrud<FacturaDto, SolicitudFactura, FiltrosFactura>(servicio);

/// <summary>Pagos de las mensualidades (<c>finanzas/recibos/recibos-http.ts</c>).</summary>
[PermisosCrud(Permisos.PagosLeer, Permisos.PagosGestionar)]
public class RecibosController(ServicioRecibos servicio) : ControladorCrud<ReciboDto, SolicitudRecibo, FiltrosRecibo>(servicio);

/// <summary>Cotizaciones (<c>finanzas/cotizaciones/cotizaciones-http.ts</c>).</summary>
[PermisosCrud(Permisos.CotizacionesLeer, Permisos.CotizacionesGestionar)]
public class CotizacionesController(ServicioCotizaciones servicio)
    : ControladorCrud<CotizacionDto, SolicitudCotizacion, FiltrosCotizacion>(servicio);

/// <summary>Recordatorios automáticos de cobro (<c>finanzas/programacion/programaciones-http.ts</c>).</summary>
[PermisosCrud(Permisos.ProgramacionesLeer, Permisos.ProgramacionesGestionar)]
[RequiereFuncionPlan(Application.Suscripciones.FuncionPlan.Recordatorios, soloEscritura: true)]
public class ProgramacionesController(ServicioProgramaciones servicio)
    : ControladorCrud<ProgramacionDto, SolicitudProgramacion, FiltrosProgramacion>(servicio)
{
    [HttpGet("{id:guid}/envios")]
    public Task<IReadOnlyList<EnvioDto>> Envios(Guid id, CancellationToken ct) => servicio.EnviosAsync(id, ct);

    /// <summary><c>?ids=a,b</c> → <c>{ "a": 3, "b": 0 }</c>.</summary>
    [HttpGet("envios/conteo")]
    public async Task<IReadOnlyDictionary<Guid, int>> ConteoEnvios([FromQuery] string? ids, CancellationToken ct)
    {
        var lista = (ids ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => Guid.TryParse(x, out var id) ? id : (Guid?)null).OfType<Guid>().Distinct().Take(1000).ToList();
        return await servicio.ConteoEnviosAsync(lista, ct);
    }
}

/// <summary>Reportes financieros calculados en el servidor (<c>finanzas/reportes/reportes-http.ts</c>).</summary>
[RequierePermiso(Permisos.ReportesLeer)]
[RequiereFuncionPlan(Application.Suscripciones.FuncionPlan.Reportes)]
public class ReportesController(ServicioReportes servicio) : ControladorApi
{
    [HttpGet("ingresos")]
    public Task<ReporteIngresosDto> Ingresos([FromQuery] FiltrosIngresos filtros, CancellationToken ct) => servicio.IngresosAsync(filtros, ct);

    [HttpGet("facturas-pendientes")]
    public Task<IReadOnlyList<CuotaPendienteDto>> Pendientes([FromQuery] FiltrosPendientes filtros, CancellationToken ct) =>
        servicio.PendientesAsync(filtros, ct);

    [HttpGet("morosos")]
    public Task<IReadOnlyList<MorosoDto>> Morosos([FromQuery] FiltrosMorosos filtros, CancellationToken ct) => servicio.MorososAsync(filtros, ct);
}

/// <summary>Pantalla de inicio del administrador (<c>inicio/dashboard/tablero-http.ts</c>).</summary>
[RequierePermiso(Permisos.TableroLeer)]
public class TableroController(ServicioTablero servicio) : ControladorApi
{
    [HttpGet]
    public Task<TableroDto> Obtener(CancellationToken ct) => servicio.ObtenerAsync(ct);
}
