using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Domain.Enums;
using RutaRola.Domain.Operacion;
using RutaRola.Domain.Personas;

namespace RutaRola.Application.Operacion;

/// <summary>
/// <c>monitoraId</c>, <c>fechaFin</c>, <c>horaInicio</c> y <c>horaFin</c> van vacíos cuando no hay (fecha fin vacía =
/// indefinida; sin horario = toda la jornada de la ruta).
/// </summary>
public record AsignacionDto(
    Guid Id, Guid RutaId, Guid VehiculoId, Guid ConductorId, string MonitoraId, DateOnly FechaInicio, DateOnly? FechaFin,
    string Estado, string Observaciones, DateTime FechaCreacion, DateTime? FechaActualizacion,
    TimeOnly? HoraInicio = null, TimeOnly? HoraFin = null);

public record SolicitudAsignacion(
    Guid? RutaId, Guid? VehiculoId, Guid? ConductorId, Guid? MonitoraId, DateOnly? FechaInicio, DateOnly? FechaFin,
    string? Estado, string? Observaciones, TimeOnly? HoraInicio = null, TimeOnly? HoraFin = null);

/// <summary>
/// <c>empleadoId</c> = conductor o monitora; <c>fecha</c> = las que cubren ese día (<c>YYYY-MM-DD</c>).
/// </summary>
public record FiltrosAsignacion(
    Guid? RutaId = null, Guid? VehiculoId = null, Guid? EmpleadoId = null, string? Estado = null, DateOnly? Fecha = null);

/// <summary>
/// Reglas de fechas y horario de las asignaciones (las mismas de <c>asignacion.ts</c> del frontend). Dos asignaciones
/// activas "se cruzan" si sus rangos de fechas se tocan (sin fin = indefinida) y su horario también: si las dos tienen
/// horario se comparan las horas; si alguna no lo tiene, ocupa toda la jornada de su ruta y se cruza con las de la
/// misma jornada.
/// </summary>
public static class ReglasAsignacion
{
    public static bool Cubre(AsignacionRuta a, DateOnly fecha) => a.FechaInicio <= fecha && (a.FechaFin is not { } fin || fin >= fecha);

    public static bool FechasSeCruzan(DateOnly inicio1, DateOnly? fin1, DateOnly inicio2, DateOnly? fin2) =>
        inicio1 <= (fin2 ?? DateOnly.MaxValue) && inicio2 <= (fin1 ?? DateOnly.MaxValue);

    public static bool HorarioSeCruza(TimeOnly? inicio1, TimeOnly? fin1, string jornada1, TimeOnly? inicio2, TimeOnly? fin2, string jornada2) =>
        inicio1 is { } i1 && fin1 is { } f1 && inicio2 is { } i2 && fin2 is { } f2
            ? i1 < f2 && i2 < f1
            : jornada1 == jornada2;

    /// <summary><c>06:00–09:30</c> o <c>""</c> sin horario.</summary>
    public static string Horario(TimeOnly? inicio, TimeOnly? fin) =>
        inicio is { } i && fin is { } f ? $"{i:HH\\:mm}–{f:HH\\:mm}" : "";

    /// <summary><c>del 2026-11-02 al 2026-11-06, 06:00–09:30</c> (para los mensajes de cruce).</summary>
    public static string Periodo(AsignacionRuta a)
    {
        var fechas = a.FechaFin is { } fin ? $"del {a.FechaInicio:yyyy-MM-dd} al {fin:yyyy-MM-dd}" : $"desde el {a.FechaInicio:yyyy-MM-dd}";
        var horario = Horario(a.HoraInicio, a.HoraFin);
        return horario.Length > 0 ? $"{fechas}, {horario}" : fechas;
    }

    /// <summary>Por hora de inicio (las que no tienen horario primero), luego por fecha de inicio.</summary>
    public static IOrderedEnumerable<AsignacionRuta> Ordenar(IEnumerable<AsignacionRuta> asignaciones) =>
        asignaciones.OrderBy(a => a.HoraInicio ?? TimeOnly.MinValue).ThenBy(a => a.FechaInicio);

    /// <summary>
    /// La asignación activa que cubre la ruta ese día. Si hay varias (turnos por horario), la del turno en curso a
    /// <paramref name="hora"/> o, si ninguno está en curso, la siguiente del día (o la primera).
    /// </summary>
    public static AsignacionRuta? Vigente(IEnumerable<AsignacionRuta> asignaciones, Guid idRuta, DateOnly fecha, TimeOnly? hora = null)
    {
        var delDia = Ordenar(asignaciones.Where(a => a.Activo && a.IdRuta == idRuta && Cubre(a, fecha))).ToList();
        if (delDia.Count <= 1 || hora is not { } h)
        {
            return delDia.FirstOrDefault();
        }
        return delDia.FirstOrDefault(a => a.HoraInicio is { } i && a.HoraFin is { } f && i <= h && h < f)
            ?? delDia.FirstOrDefault(a => a.HoraInicio is { } i && i > h)
            ?? delDia[0];
    }
}

public class ValidadorSolicitudAsignacion : AbstractValidator<SolicitudAsignacion>
{
    public ValidadorSolicitudAsignacion()
    {
        RuleFor(x => x.RutaId).NotEmpty().WithMessage("Selecciona la ruta.");
        RuleFor(x => x.VehiculoId).NotEmpty().WithMessage("Selecciona el vehículo.");
        RuleFor(x => x.ConductorId).NotEmpty().WithMessage("Selecciona el conductor.");
        RuleFor(x => x.FechaInicio).NotNull().WithMessage("Ingresa la fecha de inicio.");
        RuleFor(x => x.FechaFin).GreaterThanOrEqualTo(x => x.FechaInicio)
            .When(x => x.FechaInicio is not null && x.FechaFin is not null)
            .WithMessage("La fecha de fin no puede ser anterior a la de inicio.");
        RuleFor(x => x.Estado).EstadoRegistroValido();
        RuleFor(x => x.Observaciones).MaximumLength(500).WithMessage("Las observaciones admiten máximo 500 caracteres.");
        RuleFor(x => x.HoraFin).NotNull().When(x => x.HoraInicio is not null)
            .WithMessage("Ingresa la hora de fin del horario (o deja las dos vacías para toda la jornada).");
        RuleFor(x => x.HoraInicio).NotNull().When(x => x.HoraFin is not null)
            .WithMessage("Ingresa la hora de inicio del horario (o deja las dos vacías para toda la jornada).");
        RuleFor(x => x.HoraFin).GreaterThan(x => x.HoraInicio)
            .When(x => x.HoraInicio is not null && x.HoraFin is not null)
            .WithMessage("La hora de fin debe ser posterior a la de inicio.");
    }
}

/// <summary>
/// Vehículo + conductor (+ monitora) que cubren una ruta entre dos fechas y, opcionalmente, en un horario. Si queda
/// activa: ruta, vehículo y personal activos; capacidad suficiente; licencia y documentos sin vencer; no se cruza
/// (<see cref="ReglasAsignacion"/>) con otra asignación activa de la misma ruta, ni el vehículo, el conductor o la
/// monitora con otra asignación activa. No se elimina si ya tiene recorridos.
/// </summary>
public class ServicioAsignaciones(IBaseDatos db, TimeProvider reloj)
    : ServicioCrud<AsignacionRuta, AsignacionDto, SolicitudAsignacion, FiltrosAsignacion>(db)
{
    protected override string Entidad => "La asignación";
    protected override DbSet<AsignacionRuta> Conjunto => Db.AsignacionesRuta;

    protected override IQueryable<AsignacionRuta> Filtrar(IQueryable<AsignacionRuta> consulta, FiltrosAsignacion f)
    {
        if (f.RutaId is { } ruta)
        {
            consulta = consulta.Where(a => a.IdRuta == ruta);
        }
        if (f.VehiculoId is { } vehiculo)
        {
            consulta = consulta.Where(a => a.IdVehiculo == vehiculo);
        }
        if (f.EmpleadoId is { } empleado)
        {
            consulta = consulta.Where(a => a.IdConductor == empleado || a.IdMonitora == empleado);
        }
        if (Texto.Opcional(f.Estado) is { } estado)
        {
            var activo = EstadoRegistro.EsActivo(estado);
            consulta = consulta.Where(a => a.Activo == activo);
        }
        if (f.Fecha is { } fecha)
        {
            consulta = consulta.Where(a => a.FechaInicio <= fecha && (a.FechaFin == null || a.FechaFin >= fecha));
        }
        return consulta;
    }

    /// <summary>Activas primero; luego la más reciente y, el mismo día, por hora de inicio.</summary>
    protected override IOrderedQueryable<AsignacionRuta> Ordenar(IQueryable<AsignacionRuta> consulta) =>
        consulta.OrderByDescending(a => a.Activo).ThenByDescending(a => a.FechaInicio).ThenBy(a => a.HoraInicio).ThenBy(a => a.Id);

    protected override IQueryable<AsignacionRuta> Incluir(IQueryable<AsignacionRuta> consulta) => consulta.Include(a => a.Ruta);

    protected override string Etiqueta(AsignacionRuta a) =>
        ReglasAsignacion.Horario(a.HoraInicio, a.HoraFin) is { Length: > 0 } horario
            ? $"{ServicioRutas.Etiquetar(a.Ruta)} · {a.FechaInicio:yyyy-MM-dd} · {horario}"
            : $"{ServicioRutas.Etiquetar(a.Ruta)} · {a.FechaInicio:yyyy-MM-dd}";

    public override async Task<IReadOnlyList<Opcion>> OpcionesAsync(FiltrosAsignacion filtros, CancellationToken ct = default)
    {
        var registros = await Incluir(Ordenar(Filtrar(Conjunto.AsNoTracking(), filtros))).ToListAsync(ct);
        return registros.Select(r => new Opcion(r.Id, Etiqueta(r))).ToList();
    }

    protected override Task<IReadOnlyList<AsignacionDto>> MapearAsync(IReadOnlyList<AsignacionRuta> registros, bool detalle, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AsignacionDto>>(registros.Select(a => new AsignacionDto(
            a.Id, a.IdRuta, a.IdVehiculo, a.IdConductor, a.IdMonitora?.ToString() ?? "", a.FechaInicio, a.FechaFin,
            EstadoRegistro.Texto(a.Activo), a.Observaciones ?? "", a.FechaCreacion, a.FechaActualizacion,
            a.HoraInicio, a.HoraFin)).ToList());

    protected override async Task AplicarAsync(AsignacionRuta a, SolicitudAsignacion s, bool nuevo, CancellationToken ct)
    {
        // Referencias con el filtro de empresa: un id de otra empresa no existe (404).
        var ruta = await Db.Rutas.Include(r => r.Estudiantes).FirstOrDefaultAsync(r => r.Id == s.RutaId, ct)
            ?? throw ErrorNegocio.NoEncontrado("La ruta");
        var vehiculo = await Db.Vehiculos.Include(v => v.Documentos).FirstOrDefaultAsync(v => v.Id == s.VehiculoId, ct)
            ?? throw ErrorNegocio.NoEncontrado("El vehículo");
        var conductor = await Db.Empleados.FirstOrDefaultAsync(e => e.Id == s.ConductorId, ct)
            ?? throw ErrorNegocio.NoEncontrado("El conductor");
        Empleado? monitora = null;
        if (s.MonitoraId is { } idMonitora)
        {
            monitora = await Db.Empleados.FirstOrDefaultAsync(e => e.Id == idMonitora, ct) ?? throw ErrorNegocio.NoEncontrado("La monitora");
        }

        static ErrorNegocio Falla(string mensaje) => new(mensaje, CodigoError.Validacion);
        if (conductor.TipoEmpleado != TipoEmpleado.Conductor) throw Falla("El empleado elegido como conductor no es conductor.");
        if (monitora is not null && monitora.TipoEmpleado != TipoEmpleado.Monitora) throw Falla("El empleado elegido como monitora no es monitora.");

        var activa = EstadoRegistro.EsActivo(s.Estado);
        if (activa)
        {
            if (!ruta.Activo) throw Falla($"La ruta {ServicioRutas.Etiquetar(ruta)} está inactiva.");
            if (!vehiculo.Activo) throw Falla($"El vehículo {vehiculo.Placa} está inactivo.");
            if (!conductor.Activo) throw Falla($"{conductor.NombreCompleto} está inactivo.");
            if (monitora is { Activo: false }) throw Falla($"{monitora.NombreCompleto} está inactiva.");
            var estudiantes = ruta.Estudiantes.Count;
            if (vehiculo.Capacidad < estudiantes)
            {
                throw Falla($"El vehículo {vehiculo.Placa} tiene {vehiculo.Capacidad} puestos y la ruta lleva {estudiantes} estudiantes.");
            }
            var hoy = Fechas.Hoy(reloj);
            if (conductor.FechaVencimientoLicencia is { } licencia && licencia < hoy)
            {
                throw Falla($"La licencia de {conductor.NombreCompleto} está vencida. Actualízala en Personas › Empleados.");
            }
            var vencidos = vehiculo.Documentos.Where(d => d.FechaVencimiento < hoy).OrderBy(d => d.TipoDocumento)
                .Select(d => NombreDocumento(d.TipoDocumento)).ToList();
            if (vencidos.Count > 0)
            {
                throw Falla($"El vehículo {vehiculo.Placa} tiene vencido: {string.Join(", ", vencidos)}.");
            }
            await ValidarCrucesAsync(nuevo ? null : a.Id, ruta, vehiculo, conductor, monitora, s, ct);
        }

        a.IdRuta = ruta.Id;
        a.IdVehiculo = vehiculo.Id;
        a.IdConductor = conductor.Id;
        a.IdMonitora = monitora?.Id;
        a.FechaInicio = s.FechaInicio!.Value;
        a.FechaFin = s.FechaFin;
        a.HoraInicio = s.HoraInicio;
        a.HoraFin = s.HoraFin;
        a.Activo = activa;
        a.Observaciones = Texto.Opcional(s.Observaciones);
    }

    /// <summary>
    /// Ninguna otra asignación activa de la ruta se cruza en fechas y horario, y el vehículo, el conductor y la monitora
    /// no están en otra asignación activa que se cruce (de cualquier ruta).
    /// </summary>
    private async Task ValidarCrucesAsync(
        Guid? idActual, Ruta ruta, Vehiculo vehiculo, Empleado conductor, Empleado? monitora, SolicitudAsignacion s, CancellationToken ct)
    {
        var inicio = s.FechaInicio!.Value;
        var fin = s.FechaFin;
        var candidatas = await Db.AsignacionesRuta.Include(x => x.Ruta)
            .Where(x => x.Id != idActual && x.Activo && x.FechaInicio <= (fin ?? DateOnly.MaxValue) && (x.FechaFin == null || x.FechaFin >= inicio))
            .ToListAsync(ct);
        var cruzadas = candidatas
            .Where(x => ReglasAsignacion.HorarioSeCruza(s.HoraInicio, s.HoraFin, ruta.Jornada, x.HoraInicio, x.HoraFin, x.Ruta.Jornada))
            .OrderBy(x => x.FechaInicio).ToList();

        if (cruzadas.FirstOrDefault(x => x.IdRuta == ruta.Id) is { } misma)
        {
            throw new ErrorNegocio(
                $"La ruta {ServicioRutas.Etiquetar(ruta)} ya tiene una asignación activa {ReglasAsignacion.Periodo(misma)} que se cruza. " +
                "Cambia las fechas o el horario, o inactívala primero.", CodigoError.Duplicado);
        }
        foreach (var otra in cruzadas)
        {
            var donde = $"la ruta {ServicioRutas.Etiquetar(otra.Ruta)} {ReglasAsignacion.Periodo(otra)}";
            if (otra.IdVehiculo == vehiculo.Id)
            {
                throw new ErrorNegocio($"El vehículo {vehiculo.Placa} ya cubre {donde}.", CodigoError.Duplicado);
            }
            if (otra.IdConductor == conductor.Id || otra.IdMonitora == conductor.Id)
            {
                throw new ErrorNegocio($"{conductor.NombreCompleto} ya está asignado a {donde}.", CodigoError.Duplicado);
            }
            if (monitora is not null && (otra.IdConductor == monitora.Id || otra.IdMonitora == monitora.Id))
            {
                throw new ErrorNegocio($"{monitora.NombreCompleto} ya está asignada a {donde}.", CodigoError.Duplicado);
            }
        }
    }

    /// <summary>Nombres de <c>DOCUMENTOS_VEHICULO</c> del frontend.</summary>
    public static string NombreDocumento(TipoDocumentoVehiculo tipo) => tipo switch
    {
        TipoDocumentoVehiculo.Soat => "SOAT",
        TipoDocumentoVehiculo.Tecnomecanica => "Revisión técnico-mecánica",
        TipoDocumentoVehiculo.TarjetaOperacion => "Tarjeta de operación",
        _ => "Pólizas RCC / RCE",
    };

    public override async Task<string?> MotivoNoEliminarAsync(Guid id, CancellationToken ct = default)
    {
        var total = await Db.ViajesRuta.CountAsync(v => v.IdAsignacion == id, ct);
        return total > 0
            ? $"No se puede eliminar: la asignación tiene {total} recorrido(s) registrados. Cámbiala a Inactiva para conservar el historial."
            : null;
    }
}
