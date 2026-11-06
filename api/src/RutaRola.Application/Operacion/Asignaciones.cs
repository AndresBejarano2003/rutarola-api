using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Domain.Enums;
using RutaRola.Domain.Operacion;
using RutaRola.Domain.Personas;

namespace RutaRola.Application.Operacion;

/// <summary><c>monitoraId</c> y <c>fechaFin</c> van vacíos cuando no hay (fecha fin vacía = indefinida).</summary>
public record AsignacionDto(
    Guid Id, Guid RutaId, Guid VehiculoId, Guid ConductorId, string MonitoraId, DateOnly FechaInicio, DateOnly? FechaFin,
    string Estado, string Observaciones, DateTime FechaCreacion, DateTime? FechaActualizacion);

public record SolicitudAsignacion(
    Guid? RutaId, Guid? VehiculoId, Guid? ConductorId, Guid? MonitoraId, DateOnly? FechaInicio, DateOnly? FechaFin,
    string? Estado, string? Observaciones);

/// <summary><c>empleadoId</c> = conductor o monitora.</summary>
public record FiltrosAsignacion(Guid? RutaId = null, Guid? VehiculoId = null, Guid? EmpleadoId = null, string? Estado = null);

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
    }
}

/// <summary>
/// Vehículo + conductor (+ monitora) que cubren una ruta. Si queda activa: una sola por ruta; ruta,
/// vehículo y personal activos; capacidad suficiente; licencia y documentos sin vencer; nadie (ni el
/// vehículo) en dos rutas activas de la misma jornada. No se elimina si ya tiene recorridos.
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
        return consulta;
    }

    /// <summary>Activas primero; luego la más reciente.</summary>
    protected override IOrderedQueryable<AsignacionRuta> Ordenar(IQueryable<AsignacionRuta> consulta) =>
        consulta.OrderByDescending(a => a.Activo).ThenByDescending(a => a.FechaInicio).ThenBy(a => a.Id);

    protected override IQueryable<AsignacionRuta> Incluir(IQueryable<AsignacionRuta> consulta) => consulta.Include(a => a.Ruta);

    protected override string Etiqueta(AsignacionRuta a) => $"{ServicioRutas.Etiquetar(a.Ruta)} · {a.FechaInicio:yyyy-MM-dd}";

    public override async Task<IReadOnlyList<Opcion>> OpcionesAsync(FiltrosAsignacion filtros, CancellationToken ct = default)
    {
        var registros = await Incluir(Ordenar(Filtrar(Conjunto.AsNoTracking(), filtros))).ToListAsync(ct);
        return registros.Select(r => new Opcion(r.Id, Etiqueta(r))).ToList();
    }

    protected override Task<IReadOnlyList<AsignacionDto>> MapearAsync(IReadOnlyList<AsignacionRuta> registros, bool detalle, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AsignacionDto>>(registros.Select(a => new AsignacionDto(
            a.Id, a.IdRuta, a.IdVehiculo, a.IdConductor, a.IdMonitora?.ToString() ?? "", a.FechaInicio, a.FechaFin,
            EstadoRegistro.Texto(a.Activo), a.Observaciones ?? "", a.FechaCreacion, a.FechaActualizacion)).ToList());

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
            await ValidarCrucesAsync(nuevo ? null : a.Id, ruta, vehiculo.Id, conductor, monitora, ct);
        }

        a.IdRuta = ruta.Id;
        a.IdVehiculo = vehiculo.Id;
        a.IdConductor = conductor.Id;
        a.IdMonitora = monitora?.Id;
        a.FechaInicio = s.FechaInicio!.Value;
        a.FechaFin = s.FechaFin;
        a.Activo = activa;
        a.Observaciones = Texto.Opcional(s.Observaciones);
    }

    /// <summary>Una asignación activa por ruta; nadie (ni el vehículo) en dos rutas activas de la misma jornada.</summary>
    private async Task ValidarCrucesAsync(Guid? idActual, Ruta ruta, Guid idVehiculo, Empleado conductor, Empleado? monitora, CancellationToken ct)
    {
        var activas = await Db.AsignacionesRuta.Include(x => x.Ruta).Where(x => x.Id != idActual && x.Activo).ToListAsync(ct);
        if (activas.Any(x => x.IdRuta == ruta.Id))
        {
            throw new ErrorNegocio($"La ruta {ServicioRutas.Etiquetar(ruta)} ya tiene una asignación activa. Inactívala primero.", CodigoError.Duplicado);
        }
        foreach (var otra in activas.Where(x => x.Ruta.Jornada == ruta.Jornada))
        {
            var otraRuta = ServicioRutas.Etiquetar(otra.Ruta);
            if (otra.IdVehiculo == idVehiculo)
            {
                throw new ErrorNegocio($"El vehículo ya cubre la ruta {otraRuta} en la jornada {ruta.Jornada}.", CodigoError.Duplicado);
            }
            if (otra.IdConductor == conductor.Id || otra.IdMonitora == conductor.Id)
            {
                throw new ErrorNegocio($"{conductor.NombreCompleto} ya está asignado a la ruta {otraRuta} en la jornada {ruta.Jornada}.", CodigoError.Duplicado);
            }
            if (monitora is not null && (otra.IdConductor == monitora.Id || otra.IdMonitora == monitora.Id))
            {
                throw new ErrorNegocio($"{monitora.NombreCompleto} ya está asignada a la ruta {otraRuta} en la jornada {ruta.Jornada}.", CodigoError.Duplicado);
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
