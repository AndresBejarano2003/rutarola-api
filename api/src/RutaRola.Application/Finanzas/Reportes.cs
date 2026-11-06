using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Domain.Enums;

namespace RutaRola.Application.Finanzas;

/// <summary><c>desde</c>/<c>hasta</c>: meses <c>YYYY-MM</c> de la fecha de pago.</summary>
public record FiltrosIngresos(string? Desde = null, string? Hasta = null, string? MedioPago = null);

public record IngresoMesDto(string Periodo, decimal Total, int Cantidad);

public record IngresoMedioDto(MetodoPago MedioPago, decimal Total, int Cantidad);

public record ReporteIngresosDto(
    decimal Total, int Cantidad, decimal Promedio, IReadOnlyList<IngresoMesDto> PorMes, IReadOnlyList<IngresoMedioDto> PorMedio,
    IReadOnlyList<ReciboDto> Recibos);

/// <summary>Mensualidad de una factura vigente sin recibo válido. <c>diasMora</c> &gt; 0 = vencida.</summary>
public record CuotaPendienteDto(
    Guid FacturaId, string NumeroFactura, Guid AcudienteId, string NombreAcudiente, string NombreEstudiante, string Periodo, decimal Valor,
    DateOnly FechaLimite, int DiasMora);

/// <summary><c>situacion</c>: VENCIDAS (en mora) | POR_VENCER (dentro del plazo).</summary>
public record FiltrosPendientes(string? Texto = null, Guid? AcudienteId = null, string? Situacion = null);

public record MorosoDto(
    Guid AcudienteId, string NombreAcudiente, string Telefono, string Correo, IReadOnlyList<string> Estudiantes, IReadOnlyList<string> Facturas,
    int Cuotas, decimal ValorAdeudado, int DiasMoraMaxima, IReadOnlyList<string> Periodos);

/// <summary><c>diasMinimos</c>: mora mínima (0 = cualquiera).</summary>
public record FiltrosMorosos(string? Texto = null, int DiasMinimos = 0);

/// <summary>
/// Reportes financieros (iguales a <c>ReportesLocal</c>). Las cuotas pendientes son los meses hasta el
/// actual de las facturas vigentes sin recibo válido; la fecha límite es el día de la programación de la
/// factura o el 5 (<see cref="Cobro.FechaLimite"/>). También los usan el tablero y el portal.
/// </summary>
public class ServicioReportes(IBaseDatos db, TimeProvider reloj)
{
    public async Task<ReporteIngresosDto> IngresosAsync(FiltrosIngresos f, CancellationToken ct = default)
    {
        var consulta = db.Pagos.AsNoTracking().Where(p => p.IdFactura != null && p.Estado == EstadoPago.Vigente);
        if (TextoEnum.Leer<MetodoPago>(f.MedioPago) is { } medio)
        {
            consulta = consulta.Where(p => p.MetodoPago == medio);
        }
        DateOnly? desde = Periodos.EsValido(f.Desde) ? Periodos.Leer(f.Desde!) : null;
        DateOnly? hasta = Periodos.EsValido(f.Hasta) ? Periodos.Leer(f.Hasta!) : null;
        if (desde is { } d)
        {
            consulta = consulta.Where(p => p.FechaPago >= d);
        }
        if (hasta is { } h)
        {
            var finMes = h.AddMonths(1);
            consulta = consulta.Where(p => p.FechaPago < finMes);
        }
        var recibos = await consulta.OrderByDescending(p => p.FechaPago).ThenByDescending(p => p.Numero.Length).ThenByDescending(p => p.Numero)
            .ToListAsync(ct);

        var total = recibos.Sum(r => r.Valor);
        var meses = desde is { } inicio && hasta is { } fin
            ? Periodos.Entre(inicio, fin).ToList()
            : recibos.Select(r => Periodos.De(r.FechaPago)).Distinct().Order().ToList();
        return new ReporteIngresosDto(
            total,
            recibos.Count,
            recibos.Count > 0 ? Math.Round(total / recibos.Count, MidpointRounding.AwayFromZero) : 0,
            meses.Select(p =>
            {
                var delMes = recibos.Where(r => Periodos.De(r.FechaPago) == p).ToList();
                return new IngresoMesDto(Periodos.Texto(p), delMes.Sum(r => r.Valor), delMes.Count);
            }).ToList(),
            Enum.GetValues<MetodoPago>().Select(m =>
            {
                var delMedio = recibos.Where(r => r.MetodoPago == m).ToList();
                return new IngresoMedioDto(m, delMedio.Sum(r => r.Valor), delMedio.Count);
            }).Where(m => m.Cantidad > 0).ToList(),
            recibos.Select(ServicioRecibos.Mapear).ToList());
    }

    public async Task<IReadOnlyList<CuotaPendienteDto>> PendientesAsync(FiltrosPendientes f, CancellationToken ct = default)
    {
        var texto = Texto.Opcional(f.Texto) is { } t ? Busqueda.Normalizar(t) : null;
        var situacion = Texto.Opcional(f.Situacion);
        return (await CuotasPendientesAsync(null, ct)).Where(c =>
                (texto is null || Busqueda.Normalizar($"{c.NumeroFactura} {c.NombreAcudiente} {c.NombreEstudiante}").Contains(texto))
                && (f.AcudienteId is not { } acudiente || c.AcudienteId == acudiente)
                && (situacion is null || (situacion == "VENCIDAS") == (c.DiasMora > 0)))
            .ToList();
    }

    public async Task<IReadOnlyList<MorosoDto>> MorososAsync(FiltrosMorosos f, CancellationToken ct = default)
    {
        var vencidas = (await CuotasPendientesAsync(null, ct)).Where(c => c.DiasMora > 0).ToList();
        var ids = vencidas.Select(c => c.AcudienteId).Distinct().ToList();
        var acudientes = await db.Acudientes.AsNoTracking().Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        var texto = Texto.Opcional(f.Texto) is { } t ? Busqueda.Normalizar(t) : null;
        return vencidas.GroupBy(c => c.AcudienteId)
            .Select(g =>
            {
                var acudiente = acudientes.GetValueOrDefault(g.Key);
                return new MorosoDto(
                    g.Key, acudiente?.NombreCompleto ?? g.First().NombreAcudiente, acudiente?.Telefono ?? "", acudiente?.Correo ?? "",
                    g.Select(c => c.NombreEstudiante).Distinct().ToList(), g.Select(c => c.NumeroFactura).Distinct().ToList(), g.Count(),
                    g.Sum(c => c.Valor), g.Max(c => c.DiasMora), g.Select(c => c.Periodo).Order(StringComparer.Ordinal).ToList());
            })
            .Where(m => m.DiasMoraMaxima > Math.Max(0, f.DiasMinimos)
                && (texto is null || Busqueda.Normalizar($"{m.NombreAcudiente} {string.Join(' ', m.Estudiantes)}").Contains(texto)))
            .OrderByDescending(m => m.DiasMoraMaxima).ThenByDescending(m => m.ValorAdeudado)
            .ToList();
    }

    /// <summary>
    /// Mensualidades vencidas o del mes en curso sin recibo válido de las facturas vigentes (de un acudiente
    /// si se indica), de la fecha límite más cercana a la más lejana.
    /// </summary>
    public async Task<IReadOnlyList<CuotaPendienteDto>> CuotasPendientesAsync(Guid? idAcudiente, CancellationToken ct = default)
    {
        var hoy = Fechas.Hoy(reloj);
        var mesActual = Periodos.De(hoy);
        var facturas = await db.Facturas.AsNoTracking()
            .Where(x => x.Estado == EstadoFactura.Vigente && x.PeriodoInicio <= mesActual && (idAcudiente == null || x.IdAcudiente == idAcudiente))
            .ToListAsync(ct);
        var ids = facturas.Select(x => x.Id).ToList();
        var pagados = (await db.Pagos.AsNoTracking()
                .Where(p => p.Estado == EstadoPago.Vigente && p.IdFactura != null && ids.Contains(p.IdFactura.Value))
                .Select(p => new { p.IdFactura, p.Periodo }).ToListAsync(ct))
            .Select(p => (p.IdFactura!.Value, p.Periodo)).ToHashSet();
        var diaLimite = await db.ProgramacionesCobro.AsNoTracking().Where(p => ids.Contains(p.IdFactura))
            .ToDictionaryAsync(p => p.IdFactura, p => p.DiaLimitePago, ct);

        return facturas
            .SelectMany(fa => Periodos.Entre(fa.PeriodoInicio, fa.PeriodoFin < mesActual ? fa.PeriodoFin : mesActual)
                .Where(p => !pagados.Contains((fa.Id, p)))
                .Select(p =>
                {
                    var limite = Cobro.FechaLimite(p, diaLimite.TryGetValue(fa.Id, out var dia) ? dia : null);
                    return new CuotaPendienteDto(
                        fa.Id, fa.NumeroFactura, fa.IdAcudiente, fa.NombreAcudiente, fa.NombreEstudiante, Periodos.Texto(p), fa.ValorMensual,
                        limite, hoy.DayNumber - limite.DayNumber);
                }))
            .OrderBy(c => c.FechaLimite).ThenBy(c => c.NumeroFactura, StringComparer.Ordinal)
            .ToList();
    }
}
