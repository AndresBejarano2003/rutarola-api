using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Domain.Enums;
using RutaRola.Domain.Operacion;

namespace RutaRola.Application.Operacion;

/// <summary>Lectura del GPS del teléfono. <c>fecha</c> = momento de la lectura en el dispositivo (UTC).</summary>
public record LecturaGps(decimal? Latitud, decimal? Longitud, decimal? Velocidad, decimal? Rumbo, DateTime? Fecha);

/// <summary>Lote de lecturas (el teléfono las acumula y las envía juntas; también al recuperar la señal).</summary>
public record SolicitudLoteGps(IReadOnlyList<LecturaGps>? Posiciones);

public record PosicionDto(
    Guid RecorridoId, Guid VehiculoId, string Vehiculo, decimal Latitud, decimal Longitud, decimal? Velocidad, decimal? Rumbo, DateTime Fecha);

/// <summary><c>descartadas</c>: repetidas o fuera del periodo del recorrido. <c>ultima</c>: la más reciente guardada.</summary>
public record ResultadoLoteGps(int Recibidas, int Descartadas, PosicionDto? Ultima);

/// <summary>Posición actual de un recorrido EN_CURSO (mapa del administrador).</summary>
public record PosicionRecorridoDto(
    Guid RecorridoId, string Ruta, Trayecto Trayecto, string Conductor, string Vehiculo, ConteoParadasDto Conteo, PosicionDto? Posicion);

public class ValidadorLoteGps : AbstractValidator<SolicitudLoteGps>
{
    public const int MaximoPorLote = 500;

    public ValidadorLoteGps()
    {
        RuleFor(x => x.Posiciones).NotEmpty().WithMessage("Envía al menos una posición.")
            .Must(p => p is null || p.Count <= MaximoPorLote).WithMessage($"Envía máximo {MaximoPorLote} posiciones por lote.");
        RuleForEach(x => x.Posiciones).ChildRules(p =>
        {
            p.RuleFor(x => x.Latitud).NotNull().WithMessage("Falta la latitud.")
                .InclusiveBetween(-90, 90).WithMessage("La latitud debe estar entre -90 y 90.");
            p.RuleFor(x => x.Longitud).NotNull().WithMessage("Falta la longitud.")
                .InclusiveBetween(-180, 180).WithMessage("La longitud debe estar entre -180 y 180.");
            p.RuleFor(x => x.Velocidad).InclusiveBetween(0, 300).WithMessage("La velocidad debe estar entre 0 y 300 km/h.");
            p.RuleFor(x => x.Rumbo).InclusiveBetween(0, 360).WithMessage("El rumbo debe estar entre 0 y 360 grados.");
            p.RuleFor(x => x.Fecha).NotNull().WithMessage("Falta la fecha de la lectura.");
        });
    }
}

/// <summary>
/// Avisos en tiempo real (SignalR en el API). Si no hay nadie conectado no pasa nada; un fallo al avisar
/// nunca hace fallar la petición.
/// </summary>
public interface IAvisosRecorrido
{
    Task PosicionAsync(Guid idEmpresa, PosicionDto posicion, CancellationToken ct = default);

    /// <summary>El recorrido cambió (paradas, orden, fin): el compañero lo vuelve a consultar.</summary>
    Task RecorridoActualizadoAsync(Guid idEmpresa, Guid idRecorrido, DateTime? fechaActualizacion, CancellationToken ct = default);
}

/// <summary>Quién consulta: el administrador ve toda su empresa; el resto, solo lo suyo.</summary>
public enum AlcanceGps
{
    /// <summary>Administrador (<c>recorridos.leer</c>).</summary>
    Empresa,
    /// <summary>Conductor o monitora del recorrido (<c>gps.reportar</c>).</summary>
    Empleado,
    /// <summary>Acudiente de un estudiante del recorrido, solo mientras está EN_CURSO (<c>gps.consultar</c>).</summary>
    Acudiente,
}

/// <summary>
/// Posiciones GPS de los recorridos (tabla <c>gps_vehiculos</c>, filtrada por la empresa del vehículo).
/// Solo el conductor o la monitora del recorrido reportan; la ubicación de un vehículo con menores
/// solo la ven el administrador, el personal del recorrido y los acudientes de sus estudiantes
/// mientras el recorrido está en curso.
/// </summary>
public class ServicioGps(IBaseDatos db, IContextoUsuario contexto, IAvisosRecorrido avisos, TimeProvider reloj)
{
    /// <summary>Tolerancia del reloj del teléfono frente al inicio del recorrido y a la hora del servidor.</summary>
    public static readonly TimeSpan Tolerancia = TimeSpan.FromMinutes(5);

    public const int MaximoTrayecto = 5000;

    private const string NoEncontrado = "El recorrido no existe o no pertenece a tus rutas.";

    public async Task<ResultadoLoteGps> RegistrarAsync(Guid idRecorrido, SolicitudLoteGps s, CancellationToken ct = default)
    {
        var empleado = contexto.IdEmpleado ?? throw new ErrorNegocio(NoEncontrado, CodigoError.NoEncontrado);
        var viaje = await db.ViajesRuta.AsNoTracking()
            .Where(v => v.Id == idRecorrido && (v.IdConductor == empleado || v.IdMonitora == empleado))
            .Select(v => new { v.Id, v.IdVehiculo, v.DescripcionVehiculo, v.Estado, v.HoraInicio, v.HoraFin })
            .FirstOrDefaultAsync(ct)
            ?? throw new ErrorNegocio(NoEncontrado, CodigoError.NoEncontrado);
        if (viaje.Estado is EstadoViaje.Programado)
        {
            throw new ErrorNegocio("El recorrido aún no ha iniciado.", CodigoError.Validacion);
        }

        // Se aceptan lecturas entre el inicio y el fin (las de un teléfono sin señal llegan después de finalizar).
        var ahora = reloj.GetUtcNow().UtcDateTime;
        var desde = (viaje.HoraInicio ?? ahora) - Tolerancia;
        var hasta = viaje.Estado == EstadoViaje.EnCurso ? ahora + Tolerancia : (viaje.HoraFin ?? ahora) + Tolerancia;
        var validas = s.Posiciones!
            .Select(p => p with { Fecha = DateTime.SpecifyKind(p.Fecha!.Value.ToUniversalTime(), DateTimeKind.Utc) })
            .Where(p => p.Fecha >= desde && p.Fecha <= hasta)
            .DistinctBy(p => p.Fecha)
            .ToList();
        if (validas.Count > 0)
        {
            var minimo = validas.Min(p => p.Fecha!.Value);
            var maximo = validas.Max(p => p.Fecha!.Value);
            var existentes = (await db.GpsVehiculos.AsNoTracking()
                .Where(g => g.IdViaje == viaje.Id && g.FechaRegistro >= minimo && g.FechaRegistro <= maximo)
                .Select(g => g.FechaRegistro).ToListAsync(ct)).ToHashSet();
            validas.RemoveAll(p => existentes.Contains(p.Fecha!.Value));
        }

        if (validas.Count > 0)
        {
            db.GpsVehiculos.AddRange(validas.Select(p => new GpsVehiculo
            {
                IdViaje = viaje.Id,
                IdVehiculo = viaje.IdVehiculo,
                Latitud = Math.Round(p.Latitud!.Value, 6),
                Longitud = Math.Round(p.Longitud!.Value, 6),
                Velocidad = p.Velocidad is { } v ? Math.Round(v, 2) : null,
                Rumbo = p.Rumbo is { } r ? Math.Round(r, 2) : null,
                FechaRegistro = p.Fecha!.Value,
            }));
            await db.SaveChangesAsync(ct);
        }

        var ultima = await LeerUltimaAsync(viaje.Id, ct);
        if (validas.Count > 0 && ultima is not null && validas.Any(p => p.Fecha == ultima.Fecha) && contexto.IdEmpresa is { } empresa)
        {
            await avisos.PosicionAsync(empresa, ultima, ct);
        }
        return new ResultadoLoteGps(validas.Count, s.Posiciones!.Count - validas.Count, ultima);
    }

    /// <summary>Última posición del recorrido (null si aún no hay, o si el acudiente consulta uno que ya terminó).</summary>
    public async Task<PosicionDto?> UltimaAsync(Guid idRecorrido, AlcanceGps alcance, CancellationToken ct = default)
    {
        var enCurso = await VerificarAccesoAsync(idRecorrido, alcance, ct);
        return alcance == AlcanceGps.Acudiente && !enCurso ? null : await LeerUltimaAsync(idRecorrido, ct);
    }

    /// <summary>Trayecto recorrido en orden cronológico (máximo <see cref="MaximoTrayecto"/> puntos desde <paramref name="desde"/>).</summary>
    public async Task<IReadOnlyList<PosicionDto>> TrayectoAsync(Guid idRecorrido, AlcanceGps alcance, DateTime? desde, CancellationToken ct = default)
    {
        if (alcance == AlcanceGps.Acudiente)
        {
            throw new ErrorNegocio("No tienes permiso para realizar esta acción.", CodigoError.Prohibido);
        }
        await VerificarAccesoAsync(idRecorrido, alcance, ct);
        var consulta = db.GpsVehiculos.AsNoTracking().Where(g => g.IdViaje == idRecorrido);
        if (desde is { } d)
        {
            var utc = d.ToUniversalTime();
            consulta = consulta.Where(g => g.FechaRegistro > utc);
        }
        return await consulta.OrderBy(g => g.FechaRegistro).Take(MaximoTrayecto)
            .Select(g => new PosicionDto(g.IdViaje, g.IdVehiculo, g.Viaje.DescripcionVehiculo, g.Latitud, g.Longitud, g.Velocidad, g.Rumbo, g.FechaRegistro))
            .ToListAsync(ct);
    }

    /// <summary>Recorridos EN_CURSO de la empresa con su última posición (mapa del administrador).</summary>
    public async Task<IReadOnlyList<PosicionRecorridoDto>> EnCursoAsync(CancellationToken ct = default)
    {
        var viajes = await db.ViajesRuta.AsNoTracking().Include(v => v.Paradas)
            .Where(v => v.Estado == EstadoViaje.EnCurso).OrderBy(v => v.NombreRuta).ToListAsync(ct);
        var ids = viajes.Select(v => v.Id).ToList();
        var ultimas = await db.GpsVehiculos.AsNoTracking()
            .Where(g => ids.Contains(g.IdViaje))
            .GroupBy(g => g.IdViaje)
            .Select(grupo => grupo.OrderByDescending(g => g.FechaRegistro).First())
            .ToListAsync(ct);
        return viajes.Select(v =>
        {
            var g = ultimas.FirstOrDefault(x => x.IdViaje == v.Id);
            return new PosicionRecorridoDto(
                v.Id, v.NombreRuta, v.Trayecto, v.NombreConductor, v.DescripcionVehiculo,
                new ConteoParadasDto(
                    v.Paradas.Count,
                    v.Paradas.Count(p => p.Estado == EstadoParada.Pendiente),
                    v.Paradas.Count(p => p.Estado == EstadoParada.ABordo),
                    v.Paradas.Count(p => p.Estado == EstadoParada.Entregado),
                    v.Paradas.Count(p => p.Estado == EstadoParada.Ausente)),
                g is null ? null : new PosicionDto(v.Id, g.IdVehiculo, v.DescripcionVehiculo, g.Latitud, g.Longitud, g.Velocidad, g.Rumbo, g.FechaRegistro));
        }).ToList();
    }

    /// <summary>
    /// 404 si el recorrido no existe en la empresa o no es del empleado / de un estudiante del acudiente.
    /// Devuelve si está EN_CURSO. Lo usa también el hub para unir la conexión al grupo del recorrido.
    /// </summary>
    public async Task<bool> VerificarAccesoAsync(Guid idRecorrido, AlcanceGps alcance, CancellationToken ct = default)
    {
        var consulta = db.ViajesRuta.AsNoTracking().Where(v => v.Id == idRecorrido);
        switch (alcance)
        {
            case AlcanceGps.Empleado:
                var empleado = contexto.IdEmpleado ?? Guid.Empty;
                consulta = consulta.Where(v => v.IdConductor == empleado || v.IdMonitora == empleado);
                break;
            case AlcanceGps.Acudiente:
                var acudiente = contexto.IdAcudiente ?? Guid.Empty;
                consulta = consulta.Where(v => v.Paradas.Any(p =>
                    db.EstudiantesAcudientes.Any(ea => ea.IdEstudiante == p.IdEstudiante && ea.IdAcudiente == acudiente)));
                break;
        }
        var estado = await consulta.Select(v => (EstadoViaje?)v.Estado).FirstOrDefaultAsync(ct)
            ?? throw new ErrorNegocio(NoEncontrado, CodigoError.NoEncontrado);
        return estado == EstadoViaje.EnCurso;
    }

    private async Task<PosicionDto?> LeerUltimaAsync(Guid idRecorrido, CancellationToken ct) =>
        await db.GpsVehiculos.AsNoTracking().Where(g => g.IdViaje == idRecorrido).OrderByDescending(g => g.FechaRegistro)
            .Select(g => new PosicionDto(g.IdViaje, g.IdVehiculo, g.Viaje.DescripcionVehiculo, g.Latitud, g.Longitud, g.Velocidad, g.Rumbo, g.FechaRegistro))
            .FirstOrDefaultAsync(ct);
}
