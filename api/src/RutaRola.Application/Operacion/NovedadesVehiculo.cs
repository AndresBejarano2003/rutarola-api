using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Domain.Enums;
using RutaRola.Domain.Operacion;

namespace RutaRola.Application.Operacion;

/// <summary>
/// Novedad del vehículo (<c>operacion/vehiculos/novedades-vehiculo/novedad-vehiculo.ts</c>). <c>archivo.url</c> va
/// vacía en los listados; el detalle trae el contenido. <c>kilometraje</c> null = no se indicó. <c>delPersonal</c>: la
/// reportó un conductor o una monitora (no el administrador).
/// </summary>
public record NovedadVehiculoDto(
    Guid Id, Guid VehiculoId, string Vehiculo, TipoNovedadVehiculo Tipo, GravedadNovedad Gravedad, string Descripcion,
    int? Kilometraje, EstadoNovedadVehiculo Estado, string Reporta, bool ReportadaPorMi, string Respuesta, string Atiende,
    DateTime? FechaAtencion, ArchivoAdjuntoDto? Archivo, DateTime FechaCreacion, bool DelPersonal);

public record SolicitudNovedadVehiculo(
    Guid? VehiculoId, TipoNovedadVehiculo? Tipo, GravedadNovedad? Gravedad, string? Descripcion, int? Kilometraje,
    ArchivoAdjuntoDto? Archivo);

/// <summary>PENDIENTE → EN_PROCESO → RESUELTA (con <c>respuesta</c>); también se puede reabrir a PENDIENTE.</summary>
public record SolicitudEstadoNovedad(EstadoNovedadVehiculo? Estado, string? Respuesta);

public record FiltrosNovedadVehiculo(
    Guid? VehiculoId = null, EstadoNovedadVehiculo? Estado = null, TipoNovedadVehiculo? Tipo = null, bool? Abiertas = null);

public class ValidadorSolicitudNovedadVehiculo : AbstractValidator<SolicitudNovedadVehiculo>
{
    public ValidadorSolicitudNovedadVehiculo()
    {
        RuleFor(x => x.VehiculoId).NotEmpty().WithMessage("Elige el vehículo.");
        RuleFor(x => x.Tipo).NotNull().WithMessage("Elige el tipo de novedad.");
        RuleFor(x => x.Gravedad).NotNull().WithMessage("Indica qué tan grave es.");
        RuleFor(x => x.Descripcion).NotEmpty().WithMessage("Describe la novedad.")
            .MinimumLength(10).WithMessage("Describe la novedad con al menos 10 caracteres.")
            .MaximumLength(1000).WithMessage("La descripción admite máximo 1000 caracteres.");
        RuleFor(x => x.Kilometraje).InclusiveBetween(0, 5_000_000).When(x => x.Kilometraje is not null)
            .WithMessage("El kilometraje no es válido.");
        When(x => x.Archivo is not null, () => RuleFor(x => x.Archivo!).ReglasAdjunto("soporte"));
    }
}

public class ValidadorSolicitudEstadoNovedad : AbstractValidator<SolicitudEstadoNovedad>
{
    public ValidadorSolicitudEstadoNovedad()
    {
        RuleFor(x => x.Estado).NotNull().WithMessage("Elige el estado.");
        RuleFor(x => x.Respuesta).NotEmpty().When(x => x.Estado == EstadoNovedadVehiculo.Resuelta)
            .WithMessage("Cuenta qué se hizo para resolver la novedad.");
        RuleFor(x => x.Respuesta).MaximumLength(1000).WithMessage("La respuesta admite máximo 1000 caracteres.");
    }
}

/// <summary>
/// Novedades del vehículo. El conductor y la monitora reportan desde la Ruta del día sobre los vehículos de sus
/// asignaciones activas (que no hayan terminado) y ven las de esos vehículos; el administrador ve todas, registra y
/// las atiende. Las nuevas aparecen en la campana de los administradores (<c>GeneradorNotificaciones</c>).
/// </summary>
public class ServicioNovedadesVehiculo(IBaseDatos db, IContextoUsuario contexto, TimeProvider reloj)
{
    private const int MaximoListado = 200;

    // ---------- Administrador ----------

    public async Task<IReadOnlyList<NovedadVehiculoDto>> ListarAsync(FiltrosNovedadVehiculo f, CancellationToken ct = default) =>
        await MapearAsync(await Filtrar(db.NovedadesVehiculo.AsNoTracking(), f).ToListAsync(ct), false, ct);

    public async Task<NovedadVehiculoDto> ObtenerAsync(Guid id, CancellationToken ct = default) =>
        (await MapearAsync([await BuscarAsync(db.NovedadesVehiculo.AsNoTracking().Include(n => n.Archivo), id, ct)], true, ct))[0];

    public async Task<NovedadVehiculoDto> RegistrarAsync(SolicitudNovedadVehiculo s, CancellationToken ct = default)
    {
        if (!await db.Vehiculos.AnyAsync(v => v.Id == s.VehiculoId, ct))
        {
            throw ErrorNegocio.NoEncontrado("El vehículo");
        }
        var nombre = await NombreUsuarioAsync(ct);
        return await CrearAsync(s, null, nombre, ct);
    }

    public async Task<NovedadVehiculoDto> CambiarEstadoAsync(Guid id, SolicitudEstadoNovedad s, CancellationToken ct = default)
    {
        var novedad = await BuscarAsync(db.NovedadesVehiculo, id, ct);
        novedad.Estado = s.Estado!.Value;
        novedad.Respuesta = Texto.Opcional(s.Respuesta) ?? novedad.Respuesta;
        novedad.NombreAtiende = await NombreUsuarioAsync(ct);
        novedad.FechaAtencion = reloj.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(id, ct);
    }

    // ---------- Conductor / monitora ----------

    /// <summary>Vehículos de las asignaciones activas del empleado que no han terminado (las de hoy y las próximas).</summary>
    public async Task<IReadOnlyList<Opcion>> MisVehiculosAsync(CancellationToken ct = default)
    {
        var ids = await IdsMisVehiculosAsync(ct);
        var vehiculos = await db.Vehiculos.AsNoTracking().Where(v => ids.Contains(v.Id)).OrderBy(v => v.Placa).ToListAsync(ct);
        return vehiculos.Select(v => new Opcion(v.Id, $"{v.Placa} · Interno {v.NumeroInterno} · {v.Marca}")).ToList();
    }

    public async Task<IReadOnlyList<NovedadVehiculoDto>> MiasAsync(CancellationToken ct = default)
    {
        var ids = await IdsMisVehiculosAsync(ct);
        var empleado = IdEmpleado;
        var registros = await db.NovedadesVehiculo.AsNoTracking()
            .Where(n => ids.Contains(n.IdVehiculo) || n.IdEmpleado == empleado)
            .OrderBy(n => n.Estado == EstadoNovedadVehiculo.Resuelta).ThenByDescending(n => n.FechaCreacion)
            .Take(MaximoListado).ToListAsync(ct);
        return await MapearAsync(registros, false, ct);
    }

    public async Task<NovedadVehiculoDto> ObtenerMiaAsync(Guid id, CancellationToken ct = default)
    {
        var ids = await IdsMisVehiculosAsync(ct);
        var empleado = IdEmpleado;
        var novedad = await db.NovedadesVehiculo.AsNoTracking().Include(n => n.Archivo)
            .FirstOrDefaultAsync(n => n.Id == id && (ids.Contains(n.IdVehiculo) || n.IdEmpleado == empleado), ct)
            ?? throw ErrorNegocio.NoEncontrado("La novedad");
        return (await MapearAsync([novedad], true, ct))[0];
    }

    public async Task<NovedadVehiculoDto> ReportarAsync(SolicitudNovedadVehiculo s, CancellationToken ct = default)
    {
        var empleado = await db.Empleados.AsNoTracking().FirstOrDefaultAsync(e => e.Id == IdEmpleado, ct)
            ?? throw new ErrorNegocio("Inicia sesión como conductor o monitora para reportar novedades.", CodigoError.Prohibido);
        if (!(await IdsMisVehiculosAsync(ct)).Contains(s.VehiculoId!.Value))
        {
            throw new ErrorNegocio(
                "Solo puedes reportar novedades de los vehículos de tus rutas asignadas. Consulta con el administrador.",
                CodigoError.Validacion);
        }
        return await CrearAsync(s, empleado.Id, empleado.NombreCompleto, ct);
    }

    // ---------- Comunes ----------

    private Guid IdEmpleado => contexto.IdEmpleado
        ?? throw new ErrorNegocio("Inicia sesión como conductor o monitora para reportar novedades.", CodigoError.Prohibido);

    private async Task<List<Guid>> IdsMisVehiculosAsync(CancellationToken ct)
    {
        var empleado = IdEmpleado;
        var hoy = Fechas.Hoy(reloj);
        return await db.AsignacionesRuta.AsNoTracking()
            .Where(a => a.Activo && (a.IdConductor == empleado || a.IdMonitora == empleado) && (a.FechaFin == null || a.FechaFin >= hoy))
            .Select(a => a.IdVehiculo).Distinct().ToListAsync(ct);
    }

    private async Task<NovedadVehiculoDto> CrearAsync(SolicitudNovedadVehiculo s, Guid? idEmpleado, string reporta, CancellationToken ct)
    {
        var novedad = new NovedadVehiculo
        {
            IdVehiculo = s.VehiculoId!.Value,
            IdEmpleado = idEmpleado,
            NombreReporta = reporta,
            Tipo = s.Tipo!.Value,
            Gravedad = s.Gravedad!.Value,
            Descripcion = s.Descripcion!.Trim(),
            Kilometraje = s.Kilometraje,
        };
        novedad.IdArchivo = await Adjuntos.AplicarAsync(db, null, s.Archivo, contexto.IdUsuario, "soporte", ct);
        db.NovedadesVehiculo.Add(novedad);
        await db.SaveChangesAsync(ct);
        var creada = await db.NovedadesVehiculo.AsNoTracking().Include(n => n.Archivo).FirstAsync(n => n.Id == novedad.Id, ct);
        return (await MapearAsync([creada], true, ct))[0];
    }

    private static IQueryable<NovedadVehiculo> Filtrar(IQueryable<NovedadVehiculo> consulta, FiltrosNovedadVehiculo f)
    {
        if (f.VehiculoId is { } vehiculo) consulta = consulta.Where(n => n.IdVehiculo == vehiculo);
        if (f.Estado is { } estado) consulta = consulta.Where(n => n.Estado == estado);
        if (f.Tipo is { } tipo) consulta = consulta.Where(n => n.Tipo == tipo);
        if (f.Abiertas == true) consulta = consulta.Where(n => n.Estado != EstadoNovedadVehiculo.Resuelta);
        // Abiertas primero (las graves arriba) y luego las más recientes.
        return consulta
            .OrderBy(n => n.Estado == EstadoNovedadVehiculo.Resuelta)
            .ThenByDescending(n => n.Gravedad)
            .ThenByDescending(n => n.FechaCreacion)
            .Take(MaximoListado);
    }

    private static async Task<NovedadVehiculo> BuscarAsync(IQueryable<NovedadVehiculo> consulta, Guid id, CancellationToken ct) =>
        await consulta.FirstOrDefaultAsync(n => n.Id == id, ct) ?? throw ErrorNegocio.NoEncontrado("La novedad");

    private async Task<string> NombreUsuarioAsync(CancellationToken ct) =>
        contexto.IdUsuario is { } id
            ? await db.Usuarios.AsNoTracking().Where(u => u.Id == id).Select(u => u.Nombre).FirstOrDefaultAsync(ct) ?? "Administrador"
            : "Administrador";

    private async Task<IReadOnlyList<NovedadVehiculoDto>> MapearAsync(IReadOnlyList<NovedadVehiculo> registros, bool detalle, CancellationToken ct)
    {
        var idsVehiculos = registros.Select(n => n.IdVehiculo).Distinct().ToList();
        var placas = await db.Vehiculos.AsNoTracking().Where(v => idsVehiculos.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => $"{v.Placa} · Interno {v.NumeroInterno}", ct);
        // En el listado solo los metadatos del archivo (sin cargar su contenido).
        var idsArchivos = registros.Where(n => n.IdArchivo != null && n.Archivo is null).Select(n => n.IdArchivo!.Value).ToList();
        var archivos = idsArchivos.Count == 0
            ? new Dictionary<Guid, ArchivoAdjuntoDto>()
            : await db.Archivos.AsNoTracking().Where(a => idsArchivos.Contains(a.Id))
                .Select(a => new { a.Id, a.Nombre, a.TipoArchivo, a.Tamano })
                .ToDictionaryAsync(a => a.Id, a => new ArchivoAdjuntoDto(a.Nombre, a.TipoArchivo, a.Tamano, ""), ct);
        var empleado = contexto.IdEmpleado;
        return registros.Select(n => new NovedadVehiculoDto(
            n.Id, n.IdVehiculo, placas.GetValueOrDefault(n.IdVehiculo, ""), n.Tipo, n.Gravedad, n.Descripcion, n.Kilometraje, n.Estado,
            n.NombreReporta, empleado is not null && n.IdEmpleado == empleado, n.Respuesta ?? "", n.NombreAtiende ?? "", n.FechaAtencion,
            n.Archivo is { } a ? Adjuntos.Dto(a, detalle) : n.IdArchivo is { } idArchivo ? archivos.GetValueOrDefault(idArchivo) : null,
            n.FechaCreacion, n.IdEmpleado != null)).ToList();
    }
}
