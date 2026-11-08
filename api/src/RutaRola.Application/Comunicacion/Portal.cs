using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Application.Configuracion;
using RutaRola.Application.Finanzas;
using RutaRola.Application.Operacion;
using RutaRola.Domain.Enums;
using RutaRola.Domain.Finanzas;

namespace RutaRola.Application.Comunicacion;

/// <summary>PAGADA | VENCIDA | PENDIENTE (mes iniciado, en plazo) | PROGRAMADA (mes futuro).</summary>
public enum EstadoCuota { Pagada, Vencida, Pendiente, Programada }

public record ReciboPortalDto(
    Guid Id, string Numero, string NumeroFactura, string Estudiante, string Periodo, DateOnly FechaPago, decimal Valor, MetodoPago MedioPago,
    string Referencia, string Estado);

public record ReciboCuotaDto(Guid Id, string Numero, DateOnly FechaPago, MetodoPago MedioPago);

public record CuotaPortalDto(string Periodo, decimal Valor, EstadoCuota Estado, DateOnly FechaLimite, int DiasMora, ReciboCuotaDto? Recibo);

public record FacturaPortalDto(
    Guid Id, string Numero, DateOnly FechaEmision, Guid EstudianteId, string Estudiante, string Concepto, decimal ValorMensual, string PeriodoInicio,
    string PeriodoFin, decimal Total, decimal Pagado, decimal Vencido, string Estado, IReadOnlyList<CuotaPortalDto> Cuotas);

/// <summary>Estado del estudiante hoy en un trayecto (lo que ve el acudiente).</summary>
/// <summary><c>motivoAusencia</c>: <c>NO_ASISTE</c> | <c>POR_SU_CUENTA</c> | <c>""</c> (como en <c>ParadaDto</c>).</summary>
public record RecorridoHoyDto(
    Guid RecorridoId, Trayecto Trayecto, bool EnCurso, EstadoParada Estado, DateTime? HoraAbordo, DateTime? HoraEntrega, string MotivoAusencia);

public record TransporteEstudianteDto(
    string Ruta, string Jornada, string Sector, string Vehiculo, string Conductor, string TelefonoConductor, string Monitora, string TelefonoMonitora);

public record HorarioDto(TimeOnly Entrada, TimeOnly Salida);

public record EstudiantePortalDto(
    Guid Id, string Nombre, string Iniciales, string Grado, string Jornada, string Direccion, string Colegio, string DireccionColegio,
    HorarioDto? Horario, string Estado, TransporteEstudianteDto? Transporte, IReadOnlyList<RecorridoHoyDto> Hoy);

public record CotizacionPortalDto(
    Guid Id, string Numero, DateOnly Fecha, IReadOnlyList<string> Estudiantes, decimal ValorPorEstudiante, decimal ValorTotal, EstadoCotizacion Estado);

public record ProximoPagoDto(string NumeroFactura, string Estudiante, string Periodo, decimal Valor, DateOnly FechaLimite, EstadoCuota Estado);

public record ResumenCuentaDto(decimal Vencido, int CuotasVencidas, decimal PorPagar, ProximoPagoDto? ProximoPago, decimal PagadoAnio, int PagosAnio);

public record AcudientePortalDto(string Nombre, string Telefono, string Correo, string Direccion);

/// <summary><c>PortalAcudiente</c> del frontend (<c>portal/portal.ts</c>).</summary>
public record PortalAcudienteDto(
    AcudientePortalDto Acudiente, IReadOnlyList<EstudiantePortalDto> Estudiantes, IReadOnlyList<FacturaPortalDto> Facturas,
    IReadOnlyList<ReciboPortalDto> Recibos, IReadOnlyList<CotizacionPortalDto> Cotizaciones, ResumenCuentaDto Resumen);

/// <summary>
/// Portal del acudiente del token (nunca de un parámetro), igual que <c>PortalLocal</c>: solo SUS estudiantes
/// (colegio, horario del curso, ruta activa con su asignación vigente y recorrido de hoy), su estado de cuenta
/// con las reglas de <c>calcularEstadoCuenta</c>, los recibos de sus facturas y sus cotizaciones. Solo lectura.
/// </summary>
public class ServicioPortal(IBaseDatos db, IContextoUsuario usuario, ParametrosEmpresa parametros, TimeProvider reloj)
{
    public async Task<PortalAcudienteDto> ObtenerAsync(CancellationToken ct = default)
    {
        var idAcudiente = usuario.IdAcudiente
            ?? throw new ErrorNegocio("Inicia sesión como acudiente para ver tu portal.", CodigoError.Prohibido);
        var acudiente = await db.Acudientes.AsNoTracking().FirstOrDefaultAsync(a => a.Id == idAcudiente, ct)
            ?? throw ErrorNegocio.NoEncontrado("El acudiente");
        var hoy = Fechas.Hoy(reloj);

        var estudiantes = await db.Estudiantes.AsNoTracking().Include(e => e.Colegio).ThenInclude(c => c.Cursos)
            .Where(e => e.Acudientes.Any(x => x.IdAcudiente == idAcudiente)).ToListAsync(ct);
        var idsEstudiantes = estudiantes.Select(e => e.Id).ToList();
        var rutas = await db.Rutas.AsNoTracking().Include(r => r.Estudiantes)
            .Where(r => r.Activo && r.Estudiantes.Any(x => idsEstudiantes.Contains(x.IdEstudiante))).ToListAsync(ct);
        var idsRutas = rutas.Select(r => r.Id).ToList();
        var asignaciones = await db.AsignacionesRuta.AsNoTracking().Include(a => a.Vehiculo).Include(a => a.Conductor).Include(a => a.Monitora)
            .Where(a => a.Activo && idsRutas.Contains(a.IdRuta) && a.FechaInicio <= hoy && (a.FechaFin == null || a.FechaFin >= hoy))
            .ToListAsync(ct);
        var paradas = await db.ParadasViaje.AsNoTracking().Include(p => p.Viaje)
            .Where(p => idsEstudiantes.Contains(p.IdEstudiante) && p.Viaje.FechaViaje == hoy).ToListAsync(ct);

        var lista = estudiantes.Select(e =>
            {
                var curso = e.Colegio.Cursos.FirstOrDefault(c => c.Activo && c.Grado == e.Grado);
                var ruta = rutas.FirstOrDefault(r => r.Estudiantes.Any(x => x.IdEstudiante == e.Id));
                var asignacion = ruta is null ? null : asignaciones.FirstOrDefault(a => a.IdRuta == ruta.Id);
                return new EstudiantePortalDto(
                    e.Id, e.NombreCompleto, Iniciales(e.PrimerNombre, e.PrimerApellido), Catalogos.NombreGrado(e.Grado), e.Jornada, e.Direccion,
                    e.Colegio.RazonSocial, e.Colegio.Direccion, curso is null ? null : new HorarioDto(curso.HoraEntrada, curso.HoraSalida),
                    EstadoRegistro.Texto(e.Activo),
                    ruta is null
                        ? null
                        : new TransporteEstudianteDto(
                            ServicioRutas.Etiquetar(ruta), ruta.Jornada, ruta.Sector ?? "", asignacion?.Vehiculo.Placa ?? "",
                            asignacion?.Conductor.NombreCompleto ?? "", asignacion?.Conductor.Telefono ?? "", asignacion?.Monitora?.NombreCompleto ?? "",
                            asignacion?.Monitora?.Telefono ?? ""),
                    paradas.Where(p => p.IdEstudiante == e.Id)
                        .OrderBy(p => p.Viaje.Trayecto) // RECOGIDA antes que ENTREGA
                        .Select(p => new RecorridoHoyDto(p.IdViaje, p.Viaje.Trayecto, p.Viaje.Estado == EstadoViaje.EnCurso, p.Estado, p.HoraRecogida, p.HoraEntrega,
                            ServicioRecorridos.TextoMotivo(p.MotivoAusencia)))
                        .ToList());
            })
            // Activos primero, luego por nombre.
            .OrderBy(e => e.Estado, StringComparer.Ordinal).ThenBy(e => e.Nombre, StringComparer.CurrentCulture)
            .ToList();

        var facturas = await db.Facturas.AsNoTracking().Where(f => f.IdAcudiente == idAcudiente).ToListAsync(ct);
        var idsFacturas = facturas.Select(f => f.Id).ToList();
        var recibos = await db.Pagos.AsNoTracking().Where(p => p.IdFactura != null && idsFacturas.Contains(p.IdFactura.Value)).ToListAsync(ct);
        var diaLimite = await db.ProgramacionesCobro.AsNoTracking().Where(p => idsFacturas.Contains(p.IdFactura))
            .ToDictionaryAsync(p => p.IdFactura, p => p.DiaLimitePago, ct);
        var (facturasPortal, recibosPortal, resumen) = EstadoCuenta(facturas, recibos, diaLimite, hoy, await parametros.DiaLimitePagoAsync(ct));

        var cotizaciones = await db.Cotizaciones.AsNoTracking().Include(c => c.Estudiantes)
            .Where(c => c.IdAcudiente == idAcudiente).OrderByDescending(c => c.Fecha).ToListAsync(ct);

        return new PortalAcudienteDto(
            new AcudientePortalDto(acudiente.NombreCompleto, acudiente.Telefono, acudiente.Correo ?? "", acudiente.Direccion ?? ""),
            lista, facturasPortal, recibosPortal,
            cotizaciones.Select(c => new CotizacionPortalDto(
                c.Id, c.Numero, c.Fecha, c.Estudiantes.OrderBy(e => e.Orden).Select(e => e.Nombre).ToList(), c.ValorPorEstudiante, c.ValorTotal,
                c.Estado)).ToList(),
            resumen);
    }

    private static string Iniciales(string nombre, string apellido) =>
        $"{(nombre.Length > 0 ? nombre[0] : ' ')}{(apellido.Length > 0 ? apellido[0] : ' ')}".Trim().ToUpperInvariant();

    private static int Urgencia(EstadoCuota e) => e switch
    {
        EstadoCuota.Vencida => 0,
        EstadoCuota.Pendiente => 1,
        EstadoCuota.Programada => 2,
        _ => 3,
    };

    /// <summary>
    /// <c>calcularEstadoCuenta</c> (<c>portal/estado-cuenta.ts</c>): cada mes de una factura vigente queda PAGADA
    /// (recibo válido), VENCIDA / PENDIENTE (mes iniciado, según la fecha límite) o PROGRAMADA (mes futuro). Las
    /// facturas anuladas no tienen cuotas por cobrar y los recibos anulados no cuentan.
    /// </summary>
    public static (IReadOnlyList<FacturaPortalDto> Facturas, IReadOnlyList<ReciboPortalDto> Recibos, ResumenCuentaDto Resumen) EstadoCuenta(
        IReadOnlyList<Factura> facturas, IReadOnlyList<Pago> recibos, IReadOnlyDictionary<Guid, int> diaLimite, DateOnly hoy,
        int diaLimitePredeterminado = Cobro.DiaLimitePredeterminado)
    {
        var mesActual = Periodos.De(hoy);
        var validos = recibos.Where(r => r.Estado == EstadoPago.Vigente && r.Periodo is not null)
            .GroupBy(r => (r.IdFactura!.Value, r.Periodo!.Value)).ToDictionary(g => g.Key, g => g.First());

        var facturasPortal = facturas
            .Select(f =>
            {
                var cuotas = f.Estado != EstadoFactura.Vigente
                    ? []
                    : Periodos.Entre(f.PeriodoInicio, f.PeriodoFin).Select(p =>
                    {
                        var recibo = validos.GetValueOrDefault((f.Id, p));
                        var limite = Cobro.FechaLimite(p, diaLimite.TryGetValue(f.Id, out var dia) ? dia : null, diaLimitePredeterminado);
                        var mora = hoy.DayNumber - limite.DayNumber;
                        var estado = recibo is not null ? EstadoCuota.Pagada
                            : p > mesActual ? EstadoCuota.Programada
                            : mora > 0 ? EstadoCuota.Vencida
                            : EstadoCuota.Pendiente;
                        return new CuotaPortalDto(
                            Periodos.Texto(p), f.ValorMensual, estado, limite, estado is EstadoCuota.Vencida or EstadoCuota.Pendiente ? mora : 0,
                            recibo is null ? null : new ReciboCuotaDto(recibo.Id, recibo.Numero, recibo.FechaPago, recibo.MetodoPago));
                    }).ToList();
                decimal Suma(EstadoCuota e) => cuotas.Where(c => c.Estado == e).Sum(c => c.Valor);
                return new FacturaPortalDto(
                    f.Id, f.NumeroFactura, f.FechaEmision, f.IdEstudiante, f.NombreEstudiante, f.Concepto, f.ValorMensual, Periodos.Texto(f.PeriodoInicio),
                    Periodos.Texto(f.PeriodoFin), f.ValorTotal, Suma(EstadoCuota.Pagada), Suma(EstadoCuota.Vencida),
                    EstadoRegistro.Texto(f.Estado == EstadoFactura.Vigente), cuotas);
            })
            // Vigentes primero; luego por fecha de emisión (más reciente primero).
            .OrderBy(f => f.Estado, StringComparer.Ordinal).ThenByDescending(f => f.FechaEmision)
            .ToList();

        var recibosPortal = recibos
            .Select(r => new ReciboPortalDto(
                r.Id, r.Numero, r.NumeroFactura ?? "", r.NombreEstudiante, Periodos.Texto(r.Periodo), r.FechaPago, r.Valor, r.MetodoPago,
                r.NumeroReferencia ?? "", EstadoRegistro.Texto(r.Estado == EstadoPago.Vigente)))
            .OrderByDescending(r => r.FechaPago).ThenByDescending(r => r.Numero, StringComparer.Ordinal)
            .ToList();

        var sinPagar = facturasPortal
            .SelectMany(f => f.Cuotas.Where(c => c.Estado != EstadoCuota.Pagada)
                .Select(c => new ProximoPagoDto(f.Numero, f.Estudiante, c.Periodo, c.Valor, c.FechaLimite, c.Estado)))
            .OrderBy(c => Urgencia(c.Estado)).ThenBy(c => c.FechaLimite)
            .ToList();
        var vencidas = sinPagar.Where(c => c.Estado == EstadoCuota.Vencida).ToList();
        var pagosAnio = recibosPortal.Where(r => r.Estado == EstadoRegistro.Activo && r.FechaPago.Year == hoy.Year).ToList();

        return (facturasPortal, recibosPortal, new ResumenCuentaDto(
            vencidas.Sum(c => c.Valor), vencidas.Count, sinPagar.Where(c => c.Estado == EstadoCuota.Pendiente).Sum(c => c.Valor),
            sinPagar.FirstOrDefault(), pagosAnio.Sum(r => r.Valor), pagosAnio.Count));
    }
}
