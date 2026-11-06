using RutaRola.Application.Configuracion;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Application.Operacion;
using RutaRola.Domain.Enums;

namespace RutaRola.Application.Finanzas;

public record RecaudoDto(decimal Recaudado, decimal Esperado, int Pagos, decimal Anterior);
public record CarteraDto(decimal Vencida, int CuotasVencidas, int AcudientesEnMora, decimal PorVencer);
public record EstudiantesTableroDto(int Activos, int SinRuta, int NuevosMes, int Colegios);
public record OperacionTableroDto(int RutasActivas, int RutasSinAsignacion, int VehiculosActivos, int ConductoresActivos, int? Ocupacion);

/// <summary><c>nivel</c>: CRITICA | ADVERTENCIA | INFORMATIVA (mismos valores de las notificaciones).</summary>
public record PendienteTableroDto(string Id, NivelNotificacion Nivel, string Icono, string Titulo, string Detalle, int Cantidad, string Enlace);

/// <summary><c>tipo</c>: DOCUMENTO | LICENCIA | PAGO | CONTRATO.</summary>
public record EventoAgendaDto(DateOnly Fecha, string Tipo, string Titulo, string Detalle, string Enlace);

public record PagoRecienteDto(
    Guid Id, string Numero, string NombreAcudiente, string NombreEstudiante, string Periodo, DateOnly FechaPago, decimal Valor, MetodoPago MedioPago);

public record HistoricoDto(string Periodo, decimal Facturado, decimal Recaudado);
public record EstudiantesColegioDto(string Colegio, int Cantidad);
public record OcupacionRutaDto(string Ruta, int Estudiantes, int? Capacidad);

/// <summary><c>Tablero</c> del frontend (<c>inicio/dashboard/tablero.ts</c>).</summary>
public record TableroDto(
    string Periodo, RecaudoDto Recaudo, CarteraDto Cartera, EstudiantesTableroDto Estudiantes, OperacionTableroDto Operacion,
    IReadOnlyList<PendienteTableroDto> Pendientes, IReadOnlyList<EventoAgendaDto> Agenda, IReadOnlyList<PagoRecienteDto> UltimosPagos,
    IReadOnlyList<HistoricoDto> Historico, IReadOnlyList<EstudiantesColegioDto> EstudiantesPorColegio, IReadOnlyList<OcupacionRutaDto> OcupacionRutas);

/// <summary>Pantalla de inicio con las mismas reglas de <c>calcular-tablero.ts</c> (datos reales, nada fijo).</summary>
public class ServicioTablero(IBaseDatos db, ServicioReportes reportes, ParametrosEmpresa parametros, TimeProvider reloj)
{
    /// <summary>Días hacia adelante de la agenda (y hacia atrás de los envíos fallidos).</summary>
    public const int DiasAgenda = 30;

    private static string Plural(int n, string uno, string varios) => $"{n} {(n == 1 ? uno : varios)}";

    /// <summary>Nombres cortos de los documentos en la agenda (<c>DOCUMENTOS_VEHICULO</c> del tablero).</summary>
    private static string NombreDocumento(TipoDocumentoVehiculo t) => t switch
    {
        TipoDocumentoVehiculo.Soat => "SOAT",
        TipoDocumentoVehiculo.Tecnomecanica => "Técnico-mecánica",
        TipoDocumentoVehiculo.TarjetaOperacion => "Tarjeta de operación",
        _ => "Pólizas",
    };

    public async Task<TableroDto> ObtenerAsync(CancellationToken ct = default)
    {
        var hoy = Fechas.Hoy(reloj);
        var periodo = Periodos.De(hoy);
        var limiteAgenda = hoy.AddDays(DiasAgenda);

        var facturas = await db.Facturas.AsNoTracking().Where(f => f.Estado == EstadoFactura.Vigente).ToListAsync(ct);
        var recibos = await db.Pagos.AsNoTracking().Where(p => p.Estado == EstadoPago.Vigente && p.IdFactura != null).ToListAsync(ct);
        var cuotas = await reportes.CuotasPendientesAsync(null, ct);
        var estudiantes = await db.Estudiantes.AsNoTracking().Where(e => e.Activo).Select(e => new { e.Id, e.IdColegio, e.FechaCreacion }).ToListAsync(ct);
        var colegios = await db.Colegios.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.RazonSocial, ct);
        var rutas = await db.Rutas.AsNoTracking().Include(r => r.Estudiantes).Where(r => r.Activo).ToListAsync(ct);
        var asignaciones = await db.AsignacionesRuta.AsNoTracking().Where(a => a.Activo).ToListAsync(ct);
        var vehiculos = await db.Vehiculos.AsNoTracking().Include(v => v.Documentos).Where(v => v.Activo).ToListAsync(ct);
        var capacidades = await db.Vehiculos.AsNoTracking().ToDictionaryAsync(v => v.Id, v => v.Capacidad, ct);
        var conductores = await db.Empleados.AsNoTracking().Where(e => e.Activo && e.TipoEmpleado == TipoEmpleado.Conductor).ToListAsync(ct);
        var cotizaciones = await db.Cotizaciones.AsNoTracking().Where(c => c.Estado == EstadoCotizacion.Pendiente).ToListAsync(ct);
        var conProgramacion = (await db.ProgramacionesCobro.AsNoTracking().Where(p => p.Activo).Select(p => p.IdFactura).ToListAsync(ct)).ToHashSet();
        var desdeEnvios = DateTime.SpecifyKind(hoy.AddDays(-DiasAgenda).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc).AddHours(5);
        var enviosFallidos = await db.EnviosProgramacion.AsNoTracking().CountAsync(e => e.Estado == EstadoEnvio.Fallido && e.Fecha >= desdeEnvios, ct);

        decimal Esperado(DateOnly p) => facturas.Where(f => f.PeriodoInicio <= p && p <= f.PeriodoFin).Sum(f => f.ValorMensual);
        List<Domain.Finanzas.Pago> RecaudadoDe(DateOnly p) => recibos.Where(r => r.Periodo == p).ToList();

        // Cartera
        var vencidas = cuotas.Where(c => c.DiasMora > 0).ToList();
        var porVencer = cuotas.Where(c => c.DiasMora <= 0).ToList();
        var acudientesEnMora = vencidas.Select(c => c.AcudienteId).Distinct().Count();

        // Estudiantes y rutas
        var enRuta = rutas.SelectMany(r => r.Estudiantes.Select(e => e.IdEstudiante)).ToHashSet();
        var sinRuta = estudiantes.Count(e => !enRuta.Contains(e.Id));
        var ocupacion = rutas
            .Select(r =>
            {
                var asignacion = asignaciones.FirstOrDefault(a => a.IdRuta == r.Id);
                return new OcupacionRutaDto(ServicioRutas.Etiquetar(r), r.Estudiantes.Count,
                    asignacion is null ? null : capacidades.TryGetValue(asignacion.IdVehiculo, out var c) ? c : null);
            })
            .OrderByDescending(r => r.Capacidad is > 0 ? (double)r.Estudiantes / r.Capacidad.Value : -1)
            .ToList();
        var conVehiculo = ocupacion.Where(r => r.Capacidad is > 0).ToList();
        var cupos = conVehiculo.Sum(r => r.Capacidad!.Value);
        var rutasSinAsignacion = rutas.Count(r => asignaciones.All(a => a.IdRuta != r.Id));

        // Vencimientos
        var diasAlerta = await parametros.DiasAlertaVencimientoAsync(ct);
        var estadosVehiculo = vehiculos.Select(v => v.Documentos.Select(d => Catalogos.EstadoVencimiento(d.FechaVencimiento, hoy, diasAlerta)).ToList()).ToList();
        var vehiculosVencidos = estadosVehiculo.Count(e => e.Contains("VENCIDA"));
        var vehiculosPorVencer = estadosVehiculo.Count(e => !e.Contains("VENCIDA") && e.Contains("POR_VENCER"));
        var licencias = conductores.Select(c => Catalogos.EstadoVencimiento(c.FechaVencimientoLicencia, hoy, diasAlerta)).ToList();

        // Contratos y programación
        var siguiente = periodo.AddMonths(1);
        var contratosPorTerminar = facturas.Where(f => f.PeriodoFin == periodo || f.PeriodoFin == siguiente).ToList();
        var sinProgramacion = facturas.Count(f => f.PeriodoFin >= periodo && !conProgramacion.Contains(f.Id));

        var pendientes = new List<PendienteTableroDto>
        {
            new("mora", NivelNotificacion.Critica, "fa-solid fa-hand-holding-dollar", "Acudientes en mora",
                $"{Cobro.Pesos(vencidas.Sum(c => c.Valor))} en {Plural(vencidas.Count, "cuota vencida", "cuotas vencidas")}",
                acudientesEnMora, "/finanzas/reportes-financieros/morosos"),
            new("vehiculos-vencidos", NivelNotificacion.Critica, "fa-solid fa-bus", "Vehículos con documentos vencidos",
                "No se pueden asignar a rutas hasta renovarlos", vehiculosVencidos, "/operacion/vehiculos"),
            new("licencias-vencidas", NivelNotificacion.Critica, "fa-solid fa-id-card", "Conductores con licencia vencida",
                "No pueden tener rutas asignadas", licencias.Count(e => e == "VENCIDA"), "/operacion/conductores"),
            new("envios-fallidos", NivelNotificacion.Critica, "fa-solid fa-paper-plane", "Recordatorios de cobro fallidos",
                $"En los últimos {DiasAgenda} días; revisa los datos de contacto", enviosFallidos, "/finanzas/programacion-facturacion"),
            new("sin-ruta", NivelNotificacion.Advertencia, "fa-solid fa-user-graduate", "Estudiantes activos sin ruta",
                "Agrégalos a una ruta de su colegio y jornada", sinRuta, "/operacion/rutas"),
            new("rutas-sin-asignacion", NivelNotificacion.Advertencia, "fa-solid fa-route", "Rutas sin vehículo ni conductor",
                "Crea su asignación en Operación", rutasSinAsignacion, "/operacion/asignaciones"),
            new("vehiculos-por-vencer", NivelNotificacion.Advertencia, "fa-solid fa-file-shield", "Vehículos con documentos por vencer",
                "SOAT, técnico-mecánica, tarjeta de operación o pólizas en los próximos 30 días", vehiculosPorVencer, "/operacion/vehiculos"),
            new("licencias-por-vencer", NivelNotificacion.Advertencia, "fa-solid fa-id-card", "Licencias por vencer",
                "Vencen en los próximos 30 días", licencias.Count(e => e == "POR_VENCER"), "/operacion/conductores"),
            new("contratos-por-terminar", NivelNotificacion.Advertencia, "fa-solid fa-file-signature", "Contratos por terminar",
                "Terminan este mes o el siguiente; crea la nueva factura si continúan", contratosPorTerminar.Count, "/finanzas/facturas"),
            new("sin-programacion", NivelNotificacion.Informativa, "fa-solid fa-calendar-xmark", "Facturas sin recordatorio de cobro",
                "No tienen una programación de facturación activa", sinProgramacion, "/finanzas/programacion-facturacion"),
            new("cotizaciones", NivelNotificacion.Informativa, "fa-solid fa-file-lines", "Cotizaciones pendientes de respuesta",
                $"{Cobro.Pesos(cotizaciones.Sum(c => c.ValorTotal))} en total", cotizaciones.Count, "/finanzas/cotizaciones"),
        }.Where(p => p.Cantidad > 0).ToList();

        // Agenda de los próximos 30 días
        bool EnAgenda(DateOnly? fecha) => fecha is { } f && f >= hoy && f <= limiteAgenda;
        var agenda = new List<EventoAgendaDto>();
        foreach (var v in vehiculos)
        {
            foreach (var d in v.Documentos.OrderBy(d => d.TipoDocumento).Where(d => EnAgenda(d.FechaVencimiento)))
            {
                agenda.Add(new(d.FechaVencimiento, "DOCUMENTO", $"Vence {NombreDocumento(d.TipoDocumento)}",
                    $"Vehículo {v.Placa} · interno {v.NumeroInterno}", "/operacion/vehiculos"));
            }
        }
        foreach (var c in conductores.Where(c => EnAgenda(c.FechaVencimientoLicencia)))
        {
            agenda.Add(new(c.FechaVencimientoLicencia!.Value, "LICENCIA", "Vence licencia de conducción", c.NombreCompleto, "/operacion/conductores"));
        }
        foreach (var grupo in porVencer.Where(c => EnAgenda(c.FechaLimite)).GroupBy(c => c.FechaLimite))
        {
            agenda.Add(new(grupo.Key, "PAGO", "Fecha límite de pago",
                $"{Plural(grupo.Count(), "cuota pendiente", "cuotas pendientes")} · {Cobro.Pesos(grupo.Sum(c => c.Valor))}",
                "/finanzas/reportes-financieros/facturas-pendientes"));
        }
        foreach (var f in contratosPorTerminar)
        {
            var fecha = Periodos.Dia(f.PeriodoFin, 31);
            if (EnAgenda(fecha))
            {
                agenda.Add(new(fecha, "CONTRATO", $"Termina contrato {f.NumeroFactura}", $"{f.NombreEstudiante} · {Periodos.Nombre(f.PeriodoFin)}", "/finanzas/facturas"));
            }
        }

        var porColegio = estudiantes.GroupBy(e => e.IdColegio).ToList();
        var delMes = RecaudadoDe(periodo);
        return new TableroDto(
            Periodos.Texto(periodo),
            new RecaudoDto(delMes.Sum(r => r.Valor), Esperado(periodo), delMes.Count, RecaudadoDe(periodo.AddMonths(-1)).Sum(r => r.Valor)),
            new CarteraDto(vencidas.Sum(c => c.Valor), vencidas.Count, acudientesEnMora, porVencer.Sum(c => c.Valor)),
            new EstudiantesTableroDto(estudiantes.Count, sinRuta,
                estudiantes.Count(e => Periodos.De(DateOnly.FromDateTime(e.FechaCreacion.Add(Fechas.DesfaseColombia))) == periodo), porColegio.Count),
            new OperacionTableroDto(rutas.Count, rutasSinAsignacion, vehiculos.Count, conductores.Count,
                cupos > 0 ? (int)Math.Round(conVehiculo.Sum(r => r.Estudiantes) * 100.0 / cupos, MidpointRounding.AwayFromZero) : null),
            pendientes,
            agenda.OrderBy(a => a.Fecha).ToList(),
            recibos.OrderByDescending(r => r.FechaPago).ThenByDescending(r => r.FechaCreacion).Take(5)
                .Select(r => new PagoRecienteDto(r.Id, r.Numero, r.NombreAcudiente, r.NombreEstudiante, Periodos.Texto(r.Periodo), r.FechaPago, r.Valor, r.MetodoPago))
                .ToList(),
            Enumerable.Range(0, 12).Select(i =>
            {
                var p = periodo.AddMonths(i - 11);
                return new HistoricoDto(Periodos.Texto(p), Esperado(p), RecaudadoDe(p).Sum(r => r.Valor));
            }).ToList(),
            porColegio.Select(g => new EstudiantesColegioDto(colegios.GetValueOrDefault(g.Key) ?? "Sin colegio", g.Count()))
                .OrderByDescending(c => c.Cantidad).ToList(),
            ocupacion);
    }
}
