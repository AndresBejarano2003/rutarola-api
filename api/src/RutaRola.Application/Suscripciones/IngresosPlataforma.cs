using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Domain.Enums;

namespace RutaRola.Application.Suscripciones;

/// <summary><c>desde</c>/<c>hasta</c> <c>YYYY-MM-DD</c> (Colombia, ambos incluidos). Vacíos = mes actual.</summary>
public record FiltrosIngresos(DateOnly? Desde = null, DateOnly? Hasta = null);

/// <summary>Un punto de la serie: <c>periodo</c> <c>YYYY-MM-DD</c> (agrupación DIA) o <c>YYYY-MM</c> (MES).</summary>
public record PuntoIngresosDto(string Periodo, long Total, int Pagos);

public record GrupoIngresosDto(string Clave, string Nombre, long Total, int Pagos);

public record EmpresaIngresosDto(Guid EmpresaId, string Nombre, long Total, int Pagos);

public record PagoPlataformaDto(
    Guid Id, DateTime Fecha, Guid EmpresaId, string Empresa, PlanSuscripcion Plan, PeriodoPago Periodo, long Valor, long Descuento,
    string Metodo, string Referencia, string CodigoPromocional);

/// <summary>
/// Ingresos por planes pagados entre dos fechas (pagos APROBADOS, por la fecha en que se aplicaron; en pesos con IVA y
/// ya con el descuento del código). <c>totalAnterior</c> = el periodo inmediatamente anterior de la misma duración.
/// <c>recurrenteMensual</c> = valor mensual equivalente de los planes pagados vigentes HOY (anual / 12) con los precios
/// actuales; <c>empresasPagando</c> = cuántas empresas tienen un plan pagado vigente.
/// </summary>
public record IngresosPlataformaDto(
    DateOnly Desde, DateOnly Hasta, string Agrupacion, long Total, int Pagos, int Empresas, long TicketPromedio, long Descuentos,
    long TotalAnterior, int PagosAnterior, long RecurrenteMensual, int EmpresasPagando,
    IReadOnlyList<PuntoIngresosDto> Serie, IReadOnlyList<GrupoIngresosDto> PorPlan, IReadOnlyList<GrupoIngresosDto> PorPeriodo,
    IReadOnlyList<GrupoIngresosDto> PorMetodo, IReadOnlyList<EmpresaIngresosDto> TopEmpresas, IReadOnlyList<PagoPlataformaDto> Detalle);

/// <summary>
/// Tablero de ingresos de la plataforma (Plataforma › Ingresos). Cuenta los pagos de Wompi aprobados y los que la
/// plataforma registra a mano al activar un plan (<see cref="ServicioSuscripcion.MetodoPagoManual"/>). Serie por día
/// hasta <see cref="DiasMaximosPorDia"/> días; por mes en rangos más largos. Rango máximo <see cref="DiasMaximos"/>.
/// </summary>
public class ServicioIngresosPlataforma(IBaseDatos db, TimeProvider reloj)
{
    public const int DiasMaximosPorDia = 62;
    public const int DiasMaximos = 3 * 366;
    public const int MaximoDetalle = 500;

    public async Task<IngresosPlataformaDto> ObtenerAsync(FiltrosIngresos f, CancellationToken ct = default)
    {
        var hoy = Fechas.Hoy(reloj);
        var desde = f.Desde ?? new DateOnly(hoy.Year, hoy.Month, 1);
        var hasta = f.Hasta ?? hoy;
        if (hasta < desde)
        {
            throw new ErrorNegocio("La fecha final debe ser igual o posterior a la inicial.", CodigoError.Validacion);
        }
        var dias = hasta.DayNumber - desde.DayNumber + 1;
        if (dias > DiasMaximos)
        {
            throw new ErrorNegocio("Elige un rango de máximo 3 años.", CodigoError.Validacion);
        }
        var anteriorHasta = desde.AddDays(-1);
        var anteriorDesde = anteriorHasta.AddDays(-(dias - 1));

        var pagos = await PagosAsync(anteriorDesde, hasta, ct);
        var actuales = pagos.Where(p => p.Dia >= desde).ToList();
        var anteriores = pagos.Where(p => p.Dia < desde).ToList();
        var nombres = await db.Empresas.IgnoreQueryFilters([FiltrosConsulta.Empresa]).AsNoTracking()
            .ToDictionaryAsync(e => e.Id, e => e.Nombre, ct);

        var porDia = dias <= DiasMaximosPorDia;
        var serie = new List<PuntoIngresosDto>();
        if (porDia)
        {
            for (var d = desde; d <= hasta; d = d.AddDays(1))
            {
                var delDia = actuales.Where(p => p.Dia == d).ToList();
                serie.Add(new(d.ToString("yyyy-MM-dd"), delDia.Sum(p => p.Valor), delDia.Count));
            }
        }
        else
        {
            for (var m = new DateOnly(desde.Year, desde.Month, 1); m <= hasta; m = m.AddMonths(1))
            {
                var delMes = actuales.Where(p => p.Dia.Year == m.Year && p.Dia.Month == m.Month).ToList();
                serie.Add(new(m.ToString("yyyy-MM"), delMes.Sum(p => p.Valor), delMes.Count));
            }
        }

        var planes = await ServicioParametrosPlataforma.PlanesAsync(db, ct);
        var inicioHoy = ServicioSuscripcion.InicioDeHoyUtc(reloj.GetUtcNow().UtcDateTime);
        var vigentes = await db.Suscripciones.IgnoreQueryFilters([FiltrosConsulta.Empresa]).AsNoTracking()
            .Where(s => s.Estado == EstadoSuscripcion.Activa && s.FechaFin >= inicioHoy)
            .Select(s => new { s.Plan, s.Periodo }).ToListAsync(ct);
        var recurrente = vigentes.Sum(s =>
        {
            var plan = planes.First(p => p.Plan == s.Plan);
            return s.Periodo == PeriodoPago.Anual ? plan.PrecioAnual / 12 : plan.PrecioMensual;
        });

        var total = actuales.Sum(p => p.Valor);
        var empresas = actuales.Select(p => p.IdEmpresa).Distinct().Count();
        return new IngresosPlataformaDto(
            desde, hasta, porDia ? "DIA" : "MES", total, actuales.Count, empresas, actuales.Count > 0 ? total / actuales.Count : 0,
            actuales.Sum(p => p.Descuento), anteriores.Sum(p => p.Valor), anteriores.Count, recurrente, vigentes.Count, serie,
            Agrupar(actuales, p => TextoEnum.Escribir(p.Plan), p => planes.First(x => x.Plan == p.Plan).Nombre),
            Agrupar(actuales, p => TextoEnum.Escribir(p.Periodo), p => p.Periodo == PeriodoPago.Anual ? "Anual" : "Mensual"),
            Agrupar(actuales, p => p.Metodo, p => NombreMetodo(p.Metodo)),
            actuales.GroupBy(p => p.IdEmpresa)
                .Select(g => new EmpresaIngresosDto(g.Key, nombres.GetValueOrDefault(g.Key, ""), g.Sum(p => p.Valor), g.Count()))
                .OrderByDescending(e => e.Total).Take(10).ToList(),
            actuales.OrderByDescending(p => p.Fecha).Take(MaximoDetalle)
                .Select(p => new PagoPlataformaDto(p.Id, p.Fecha, p.IdEmpresa, nombres.GetValueOrDefault(p.IdEmpresa, ""), p.Plan, p.Periodo,
                    p.Valor, p.Descuento, p.Metodo, p.Referencia, p.Codigo))
                .ToList());
    }

    private sealed record PagoLeido(
        Guid Id, Guid IdEmpresa, DateTime Fecha, DateOnly Dia, PlanSuscripcion Plan, PeriodoPago Periodo, long Valor, long Descuento,
        string Metodo, string Referencia, string Codigo);

    private async Task<List<PagoLeido>> PagosAsync(DateOnly desde, DateOnly hasta, CancellationToken ct)
    {
        var inicio = DateTime.SpecifyKind(desde.ToDateTime(TimeOnly.MinValue) - Fechas.DesfaseColombia, DateTimeKind.Utc);
        var fin = DateTime.SpecifyKind(hasta.AddDays(1).ToDateTime(TimeOnly.MinValue) - Fechas.DesfaseColombia, DateTimeKind.Utc);
        var filas = await db.PagosSuscripcion.IgnoreQueryFilters([FiltrosConsulta.Empresa]).AsNoTracking()
            .Where(p => p.Estado == EstadoPagoSuscripcion.Aprobado && p.ValorCentavos > 0
                && (p.FechaAplicacion ?? p.FechaCreacion) >= inicio && (p.FechaAplicacion ?? p.FechaCreacion) < fin)
            .Select(p => new
            {
                p.Id, p.IdEmpresa, Fecha = p.FechaAplicacion ?? p.FechaCreacion, p.Plan, p.Periodo, p.ValorCentavos, p.DescuentoCentavos,
                p.MetodoPago, p.Referencia, p.CodigoPromocional,
            })
            .ToListAsync(ct);
        return filas.Select(p => new PagoLeido(
            p.Id, p.IdEmpresa, p.Fecha, DateOnly.FromDateTime(p.Fecha.Add(Fechas.DesfaseColombia)), p.Plan, p.Periodo, p.ValorCentavos / 100,
            p.DescuentoCentavos / 100, string.IsNullOrWhiteSpace(p.MetodoPago) ? "OTRO" : p.MetodoPago.ToUpperInvariant(), p.Referencia,
            p.CodigoPromocional ?? "")).ToList();
    }

    private static List<GrupoIngresosDto> Agrupar(IEnumerable<PagoLeido> pagos, Func<PagoLeido, string> clave, Func<PagoLeido, string> nombre) =>
        pagos.GroupBy(clave)
            .Select(g => new GrupoIngresosDto(g.Key, nombre(g.First()), g.Sum(p => p.Valor), g.Count()))
            .OrderByDescending(g => g.Total).ToList();

    /// <summary>Nombre del método (Wompi: CARD, NEQUI, PSE…; MANUAL = registrado por la plataforma).</summary>
    public static string NombreMetodo(string metodo) => metodo switch
    {
        ServicioSuscripcion.MetodoPagoManual => "Registrado por la plataforma",
        "CARD" => "Tarjeta",
        "NEQUI" => "Nequi",
        "PSE" => "PSE",
        "BANCOLOMBIA_TRANSFER" => "Transferencia Bancolombia",
        "BANCOLOMBIA_QR" => "QR Bancolombia",
        "CODIGO_PROMOCIONAL" => "Código promocional",
        "OTRO" => "Otro",
        _ => metodo,
    };
}
