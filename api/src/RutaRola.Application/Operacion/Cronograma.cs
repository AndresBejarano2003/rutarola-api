using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Application.Configuracion;
using RutaRola.Application.Finanzas;
using RutaRola.Domain.Enums;

namespace RutaRola.Application.Operacion;

/// <summary><c>desde</c>/<c>hasta</c> <c>YYYY-MM-DD</c> (ambos incluidos, máximo <see cref="ServicioCronograma.DiasMaximos"/> días).</summary>
public record FiltrosCronograma(DateOnly? Desde = null, DateOnly? Hasta = null);

/// <summary>
/// Hecho de la agenda: <c>tipo</c> DOCUMENTO | LICENCIA | PAGO | CONTRATO | FIN_ASIGNACION; <c>nivel</c> CRITICA (vencido),
/// ADVERTENCIA o INFORMATIVA.
/// </summary>
public record EventoCronogramaDto(string Tipo, NivelNotificacion Nivel, string Titulo, string Detalle, string Enlace);

/// <summary>
/// Resumen de un día: <c>habil</c> = lunes a viernes; <c>rutas</c> = rutas programadas (activas con asignación vigente ese
/// día, solo días hábiles, o las que tuvieron recorrido); recorridos finalizados / en curso; avisos de ausencia.
/// </summary>
public record DiaCronogramaDto(
    DateOnly Fecha, bool Habil, int Rutas, int Finalizados, int EnCurso, int Ausencias, IReadOnlyList<EventoCronogramaDto> Eventos);

public record CronogramaDto(DateOnly Desde, DateOnly Hasta, IReadOnlyList<DiaCronogramaDto> Dias);

/// <summary>
/// Estado de un trayecto en el día: PROGRAMADO (aún no inicia), EN_CURSO, FINALIZADO o SIN_REGISTRO (día pasado sin recorrido).
/// <c>recorridoId</c> <c>""</c> si no hay recorrido.
/// </summary>
public record TrayectoCronogramaDto(Trayecto Trayecto, string Estado, string RecorridoId, string HoraInicio, string HoraFin);

public record RutaCronogramaDto(
    Guid RutaId, string Codigo, string Nombre, string Jornada, string Colegio, string Vehiculo, string Conductor, string Monitora,
    int Estudiantes, int Ausencias, IReadOnlyList<TrayectoCronogramaDto> Trayectos);

/// <summary>Aviso de ausencia del acudiente: <c>trayecto</c> <c>""</c> = recogida y entrega.</summary>
public record AusenciaCronogramaDto(
    Guid Id, string Estudiante, string Ruta, string Trayecto, MotivoAusencia Motivo, string Nota, string Acudiente, string ConfirmadoPor);

public record DetalleDiaCronogramaDto(
    DateOnly Fecha, bool Habil, IReadOnlyList<RutaCronogramaDto> Rutas, IReadOnlyList<AusenciaCronogramaDto> Ausencias,
    IReadOnlyList<EventoCronogramaDto> Eventos);

/// <summary>
/// Cronograma de la operación (Operación › Cronograma, planes con <c>CRONOGRAMA</c>): por día, las rutas programadas, los
/// recorridos realizados, los avisos de ausencia de los acudientes y la agenda (vencimientos de documentos y licencias,
/// fechas límite de pago, fin de contratos y de asignaciones). Mismas reglas que <c>calcular-cronograma.ts</c>.
/// </summary>
public class ServicioCronograma(IBaseDatos db, ParametrosEmpresa parametros, TimeProvider reloj)
{
    public const int DiasMaximos = 400;

    public static bool EsHabil(DateOnly d) => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    public async Task<CronogramaDto> ObtenerAsync(FiltrosCronograma f, CancellationToken ct = default)
    {
        var (desde, hasta) = Rango(f.Desde, f.Hasta);
        var datos = await LeerAsync(desde, hasta, ct);
        var dias = new List<DiaCronogramaDto>();
        for (var d = desde; d <= hasta; d = d.AddDays(1))
        {
            var viajes = datos.Viajes.Where(v => v.FechaViaje == d).ToList();
            var programadas = RutasDelDia(datos, d).Select(r => r.Id).Concat(viajes.Select(v => v.IdRuta)).Distinct().Count();
            dias.Add(new(d, EsHabil(d), programadas, viajes.Count(v => v.Estado == EstadoViaje.Finalizado),
                viajes.Count(v => v.Estado == EstadoViaje.EnCurso), datos.Avisos.Count(a => a.Fecha == d), Eventos(datos, d)));
        }
        return new CronogramaDto(desde, hasta, dias);
    }

    public async Task<DetalleDiaCronogramaDto> DiaAsync(DateOnly? fecha, CancellationToken ct = default)
    {
        var d = fecha ?? Fechas.Hoy(reloj);
        var datos = await LeerAsync(d, d, ct);
        var hoy = Fechas.Hoy(reloj);
        var viajes = datos.Viajes.Where(v => v.FechaViaje == d).ToList();
        var avisos = datos.Avisos.Where(a => a.Fecha == d).ToList();
        var rutaDeEstudiante = datos.Rutas.SelectMany(r => r.Estudiantes.Where(e => e.Activo).Select(e => (e.IdEstudiante, r))).ToLookup(x => x.IdEstudiante, x => x.r);
        var conViaje = viajes.Select(v => v.IdRuta).ToHashSet();

        var rutas = RutasDelDia(datos, d).Concat(datos.Rutas.Where(r => conViaje.Contains(r.Id))).DistinctBy(r => r.Id)
            .OrderBy(r => r.Jornada, StringComparer.Ordinal).ThenBy(r => r.Codigo, StringComparer.Ordinal)
            .Select(r =>
            {
                var asignacion = ReglasAsignacion.Vigente(datos.Asignaciones, r.Id, d);
                var estudiantes = r.Estudiantes.Where(e => e.Activo).Select(e => e.IdEstudiante).ToHashSet();
                var trayectos = new[] { Trayecto.Recogida, Trayecto.Entrega }.Select(t =>
                {
                    var v = viajes.FirstOrDefault(x => x.IdRuta == r.Id && x.Trayecto == t);
                    var estado = v is null ? (d < hoy ? "SIN_REGISTRO" : "PROGRAMADO")
                        : v.Estado == EstadoViaje.Finalizado ? "FINALIZADO" : v.Estado == EstadoViaje.EnCurso ? "EN_CURSO" : "PROGRAMADO";
                    return new TrayectoCronogramaDto(t, estado, v?.Id.ToString() ?? "", Hora(v?.HoraInicio), Hora(v?.HoraFin));
                }).ToList();
                return new RutaCronogramaDto(
                    r.Id, r.Codigo, r.Nombre, r.Jornada, datos.Colegios.GetValueOrDefault(r.IdColegio, ""),
                    asignacion is null ? "" : datos.Vehiculos.FirstOrDefault(v => v.Id == asignacion.IdVehiculo)?.Placa ?? "",
                    asignacion is null ? "" : datos.Empleados.GetValueOrDefault(asignacion.IdConductor, ""),
                    asignacion?.IdMonitora is { } m ? datos.Empleados.GetValueOrDefault(m, "") : "",
                    estudiantes.Count, avisos.Count(a => estudiantes.Contains(a.IdEstudiante)), trayectos);
            }).ToList();

        var ausencias = avisos
            .Select(a => new AusenciaCronogramaDto(
                a.Id, datos.Estudiantes.GetValueOrDefault(a.IdEstudiante, ""),
                string.Join(", ", rutaDeEstudiante[a.IdEstudiante].Select(r => r.Codigo)),
                a.Trayecto is { } t ? TextoEnum.Escribir(t) : "", a.Motivo, a.Nota ?? "", datos.Acudientes.GetValueOrDefault(a.IdAcudiente, ""),
                a.NombreConfirmadoPor ?? ""))
            .OrderBy(a => a.Ruta, StringComparer.Ordinal).ThenBy(a => a.Estudiante, StringComparer.Ordinal).ToList();
        return new DetalleDiaCronogramaDto(d, EsHabil(d), rutas, ausencias, Eventos(datos, d));
    }

    // --- Apoyo -------------------------------------------------------------------------------------

    private static (DateOnly, DateOnly) Rango(DateOnly? desde, DateOnly? hasta)
    {
        if (desde is not { } d || hasta is not { } h)
        {
            throw new ErrorNegocio("Indica el rango de fechas.", CodigoError.Validacion);
        }
        if (h < d) throw new ErrorNegocio("La fecha final debe ser igual o posterior a la inicial.", CodigoError.Validacion);
        if (h.DayNumber - d.DayNumber + 1 > DiasMaximos) throw new ErrorNegocio("Elige un rango de máximo un año.", CodigoError.Validacion);
        return (d, h);
    }

    private static string Hora(DateTime? utc) => utc is { } u ? u.Add(Fechas.DesfaseColombia).ToString("HH:mm") : "";

    private static bool Cubre(Domain.Operacion.AsignacionRuta a, DateOnly d) => ReglasAsignacion.Cubre(a, d);

    private static IEnumerable<Domain.Operacion.Ruta> RutasDelDia(Datos datos, DateOnly d) =>
        !EsHabil(d) ? [] : datos.Rutas.Where(r => datos.Asignaciones.Any(a => a.IdRuta == r.Id && Cubre(a, d)));

    private sealed record Datos(
        List<Domain.Operacion.Ruta> Rutas, List<Domain.Operacion.AsignacionRuta> Asignaciones, List<Domain.Operacion.ViajeRuta> Viajes,
        List<Domain.Operacion.AvisoAusencia> Avisos, List<Domain.Operacion.Vehiculo> Vehiculos, List<Domain.Personas.Empleado> Conductores,
        Dictionary<Guid, string> Empleados, Dictionary<Guid, string> Colegios, Dictionary<Guid, string> Estudiantes,
        Dictionary<Guid, string> Acudientes, List<Domain.Finanzas.Factura> Facturas, Dictionary<Guid, List<PagoMes>> Pagados,
        Dictionary<Guid, int> DiaLimite, int DiaLimitePredeterminado, DateOnly Hoy);

    private async Task<Datos> LeerAsync(DateOnly desde, DateOnly hasta, CancellationToken ct)
    {
        var rutas = await db.Rutas.AsNoTracking().Include(r => r.Estudiantes).Where(r => r.Activo).ToListAsync(ct);
        var asignaciones = await db.AsignacionesRuta.AsNoTracking()
            .Where(a => a.Activo && a.FechaInicio <= hasta && (a.FechaFin == null || a.FechaFin >= desde)).ToListAsync(ct);
        var viajes = await db.ViajesRuta.AsNoTracking().Where(v => v.FechaViaje >= desde && v.FechaViaje <= hasta).ToListAsync(ct);
        var avisos = await db.AvisosAusencia.AsNoTracking().Where(a => a.Fecha >= desde && a.Fecha <= hasta).ToListAsync(ct);
        var vehiculos = await db.Vehiculos.AsNoTracking().Include(v => v.Documentos).Where(v => v.Activo).ToListAsync(ct);
        var empleados = await db.Empleados.AsNoTracking().ToListAsync(ct);
        var colegios = await db.Colegios.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.RazonSocial, ct);
        var idsEstudiantes = avisos.Select(a => a.IdEstudiante).Distinct().ToList();
        var estudiantes = (await db.Estudiantes.AsNoTracking().Where(e => idsEstudiantes.Contains(e.Id)).ToListAsync(ct))
            .ToDictionary(e => e.Id, e => e.NombreCompleto);
        var idsAcudientes = avisos.Select(a => a.IdAcudiente).Distinct().ToList();
        var acudientes = (await db.Acudientes.AsNoTracking().Where(a => idsAcudientes.Contains(a.Id)).ToListAsync(ct))
            .ToDictionary(a => a.Id, a => a.NombreCompleto);
        var mesDesde = Periodos.De(desde);
        var mesHasta = Periodos.De(hasta);
        var facturas = await db.Facturas.AsNoTracking()
            .Where(x => x.Estado == EstadoFactura.Vigente && x.PeriodoInicio <= mesHasta && x.PeriodoFin >= mesDesde).ToListAsync(ct);
        var ids = facturas.Select(x => x.Id).ToList();
        var pagados = await ReglasCuotas.PagosAsync(db, ids, ct);
        var diaLimite = await db.ProgramacionesCobro.AsNoTracking().Where(p => ids.Contains(p.IdFactura))
            .ToDictionaryAsync(p => p.IdFactura, p => p.DiaLimitePago, ct);
        return new Datos(
            rutas, asignaciones, viajes, avisos, vehiculos,
            empleados.Where(e => e.Activo && e.TipoEmpleado == TipoEmpleado.Conductor).ToList(),
            empleados.ToDictionary(e => e.Id, e => e.NombreCompleto), colegios, estudiantes, acudientes, facturas, pagados, diaLimite,
            await parametros.DiaLimitePagoAsync(ct), Fechas.Hoy(reloj));
    }

    private static string NombreDocumento(TipoDocumentoVehiculo t) => t switch
    {
        TipoDocumentoVehiculo.Soat => "SOAT",
        TipoDocumentoVehiculo.Tecnomecanica => "Técnico-mecánica",
        TipoDocumentoVehiculo.TarjetaOperacion => "Tarjeta de operación",
        TipoDocumentoVehiculo.Polizas => "Pólizas",
        _ => "Documento",
    };

    private static List<EventoCronogramaDto> Eventos(Datos datos, DateOnly d)
    {
        var pasado = d < datos.Hoy;
        var nivelVence = pasado ? NivelNotificacion.Critica : NivelNotificacion.Advertencia;
        var eventos = new List<EventoCronogramaDto>();
        foreach (var v in datos.Vehiculos)
        {
            foreach (var doc in v.Documentos.Where(x => x.FechaVencimiento == d && x.TipoDocumento != TipoDocumentoVehiculo.Otro).OrderBy(x => x.TipoDocumento))
            {
                eventos.Add(new("DOCUMENTO", nivelVence, $"Vence {NombreDocumento(doc.TipoDocumento)}", $"Vehículo {v.Placa} · interno {v.NumeroInterno}",
                    "/operacion/vehiculos"));
            }
        }
        foreach (var c in datos.Conductores.Where(c => c.FechaVencimientoLicencia == d))
        {
            eventos.Add(new("LICENCIA", nivelVence, "Vence licencia de conducción", c.NombreCompleto, "/operacion/conductores"));
        }
        // Fecha límite de pago de cada mensualidad (sin los meses desde el retiro; las pagadas no cuentan como pendientes y
        // un abono deja pendiente solo el saldo).
        var mes = Periodos.De(d);
        var cuotas = datos.Facturas
            .Select(fa => (Factura: fa, Cuota: ReglasCuotas.Cuotas(fa, datos.Pagados.GetValueOrDefault(fa.Id) ?? []).FirstOrDefault(c => c.Periodo == mes)))
            .Where(x => x.Cuota is not null)
            .Select(x => (x.Factura, Cuota: x.Cuota!, Limite: Cobro.FechaLimite(mes, datos.DiaLimite.TryGetValue(x.Factura.Id, out var dia) ? dia : null,
                datos.DiaLimitePredeterminado)))
            .Where(x => x.Limite == d).ToList();
        if (cuotas.Count > 0)
        {
            var pendientes = cuotas.Where(x => !x.Cuota.Pagada).ToList();
            eventos.Add(new("PAGO", pendientes.Count == 0 ? NivelNotificacion.Informativa : pasado ? NivelNotificacion.Critica : NivelNotificacion.Advertencia,
                "Fecha límite de pago",
                $"{Plural(cuotas.Count, "cuota", "cuotas")} · {Plural(pendientes.Count, "pendiente", "pendientes")} · {Cobro.Pesos(pendientes.Sum(x => x.Cuota.Saldo))}",
                "/finanzas/reportes-financieros/facturas-pendientes"));
        }
        foreach (var fa in datos.Facturas)
        {
            if (ReglasCuotas.UltimoMesCobrable(fa.PeriodoInicio, fa.PeriodoFin, fa.PeriodoRetiro) is not { } ultimo || Periodos.Dia(ultimo, 31) != d) continue;
            eventos.Add(new("CONTRATO", NivelNotificacion.Informativa, $"Termina contrato {fa.NumeroFactura}", $"{fa.NombreEstudiante} · {Periodos.Nombre(ultimo)}",
                "/finanzas/facturas"));
        }
        foreach (var a in datos.Asignaciones.Where(a => a.FechaFin == d))
        {
            var ruta = datos.Rutas.FirstOrDefault(r => r.Id == a.IdRuta);
            if (ruta is null) continue;
            eventos.Add(new("FIN_ASIGNACION", NivelNotificacion.Informativa, $"Termina la asignación de {ruta.Codigo}",
                $"{ruta.Nombre} · {datos.Empleados.GetValueOrDefault(a.IdConductor, "")}", "/operacion/asignaciones"));
        }
        return eventos;
    }

    private static string Plural(int n, string uno, string varios) => $"{n} {(n == 1 ? uno : varios)}";
}
