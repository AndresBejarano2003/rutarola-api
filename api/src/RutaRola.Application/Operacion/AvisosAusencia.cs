using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Domain.Enums;
using RutaRola.Domain.Operacion;

namespace RutaRola.Application.Operacion;

/// <summary>
/// Aviso del acudiente: su estudiante no viaja en la ruta una fecha. <c>trayecto</c> <c>""</c> = recogida y entrega
/// (siempre así con NO_ASISTE). <c>confirmado</c>: la monitora o el conductor ya lo marcaron AUSENTE en el recorrido.
/// </summary>
public record AvisoAusenciaDto(
    Guid Id, Guid EstudianteId, string Estudiante, DateOnly Fecha, string Trayecto, MotivoAusencia Motivo, string Nota, string Acudiente,
    string TelefonoAcudiente, string Ruta, bool Confirmado, string ConfirmadoPor, DateTime? FechaConfirmacion, DateTime FechaCreacion);

/// <summary>El aviso tal como lo ve el personal en la parada del recorrido.</summary>
public record AvisoParadaDto(Guid Id, MotivoAusencia Motivo, string Nota, string Acudiente, DateTime Fecha, bool Confirmado, string ConfirmadoPor);

/// <summary><c>trayecto</c> null = ambos (obligatorio null con NO_ASISTE).</summary>
public record SolicitudAvisoAusencia(Guid? EstudianteId, DateOnly? Fecha, Trayecto? Trayecto, MotivoAusencia? Motivo, string? Nota);

public record FiltrosAvisoAusencia(DateOnly? Fecha = null, Guid? RutaId = null);

public class ValidadorSolicitudAvisoAusencia : AbstractValidator<SolicitudAvisoAusencia>
{
    public ValidadorSolicitudAvisoAusencia()
    {
        RuleFor(x => x.EstudianteId).NotEmpty().WithMessage("Elige el estudiante.");
        RuleFor(x => x.Fecha).NotNull().WithMessage("Elige la fecha.");
        RuleFor(x => x.Motivo).NotNull().WithMessage("Indica si no va al colegio o solo no usa la ruta.");
        RuleFor(x => x.Trayecto).Null().When(x => x.Motivo == MotivoAusencia.NoAsiste)
            .WithMessage("Si no va al colegio, el aviso aplica a la recogida y a la entrega.");
        RuleFor(x => x.Nota).MaximumLength(300).WithMessage("La nota admite máximo 300 caracteres.");
    }
}

/// <summary>
/// Avisos de ausencia (tabla <c>avisos_ausencia</c>). El acudiente sale SIEMPRE del token y solo avisa por SUS
/// estudiantes, de hoy a <see cref="DiasMaximos"/> días. El personal lo ve en la parada del recorrido (badge "El
/// acudiente avisó") y lo confirma al marcarlo AUSENTE; el administrador los ve en Operación › Asignaciones.
/// </summary>
public class ServicioAvisosAusencia(IBaseDatos db, IContextoUsuario contexto, IAvisosRecorrido avisos, TimeProvider reloj)
{
    public const int DiasMaximos = 30;

    private Guid IdAcudiente => contexto.IdAcudiente
        ?? throw new ErrorNegocio("Inicia sesión como acudiente para avisar una ausencia.", CodigoError.Prohibido);

    /// <summary>Avisos de hoy en adelante de los estudiantes del acudiente del token (el portal los trae también).</summary>
    public async Task<IReadOnlyList<AvisoAusenciaDto>> DelAcudienteAsync(CancellationToken ct = default)
    {
        var idAcudiente = IdAcudiente;
        var hoy = Fechas.Hoy(reloj);
        var estudiantes = db.EstudiantesAcudientes.Where(x => x.IdAcudiente == idAcudiente).Select(x => x.IdEstudiante);
        return await ListarAsync(db.AvisosAusencia.Where(a => estudiantes.Contains(a.IdEstudiante) && a.Fecha >= hoy), ct);
    }

    /// <summary>Administrador: avisos de una fecha (hoy por defecto), opcionalmente de una ruta.</summary>
    public async Task<IReadOnlyList<AvisoAusenciaDto>> ListarAsync(FiltrosAvisoAusencia f, CancellationToken ct = default)
    {
        var fecha = f.Fecha ?? Fechas.Hoy(reloj);
        var consulta = db.AvisosAusencia.Where(a => a.Fecha == fecha);
        if (f.RutaId is { } ruta)
        {
            var enRuta = db.RutasEstudiantes.Where(r => r.IdRuta == ruta && r.Activo).Select(r => r.IdEstudiante);
            consulta = consulta.Where(a => enRuta.Contains(a.IdEstudiante));
        }
        return await ListarAsync(consulta, ct);
    }

    public async Task<AvisoAusenciaDto> CrearAsync(SolicitudAvisoAusencia s, CancellationToken ct = default)
    {
        var idAcudiente = IdAcudiente;
        var hoy = Fechas.Hoy(reloj);
        var fecha = s.Fecha!.Value;
        var idEstudiante = s.EstudianteId!.Value;
        if (!await db.EstudiantesAcudientes.AnyAsync(x => x.IdAcudiente == idAcudiente && x.IdEstudiante == idEstudiante, ct))
        {
            throw ErrorNegocio.NoEncontrado("El estudiante");
        }
        if (fecha < hoy || fecha > hoy.AddDays(DiasMaximos))
        {
            throw new ErrorNegocio($"Puedes avisar desde hoy y hasta {DiasMaximos} días adelante.", CodigoError.Validacion);
        }
        var trayecto = s.Motivo == MotivoAusencia.NoAsiste ? null : s.Trayecto;
        if (await db.AvisosAusencia.AnyAsync(a => a.IdEstudiante == idEstudiante && a.Fecha == fecha
                && (a.Trayecto == null || trayecto == null || a.Trayecto == trayecto), ct))
        {
            throw new ErrorNegocio("Ya hay un aviso para ese día. Cancélalo si quieres cambiarlo.", CodigoError.Duplicado);
        }
        // Un trayecto que ya terminó no se puede avisar (ya se sabe si viajó o no).
        var terminados = await db.ParadasViaje.Where(p => p.IdEstudiante == idEstudiante && p.Viaje.FechaViaje == fecha
                && p.Viaje.Estado == EstadoViaje.Finalizado && (trayecto == null || p.Viaje.Trayecto == trayecto))
            .Select(p => p.Viaje.Trayecto).Distinct().ToListAsync(ct);
        if (trayecto is not null ? terminados.Count > 0 : terminados.Count >= 2)
        {
            throw new ErrorNegocio("Ese recorrido ya terminó: no se puede avisar.", CodigoError.Validacion);
        }

        var aviso = new AvisoAusencia
        {
            IdEstudiante = idEstudiante,
            IdAcudiente = idAcudiente,
            Fecha = fecha,
            Trayecto = trayecto,
            Motivo = s.Motivo!.Value,
            Nota = Texto.Opcional(s.Nota),
        };
        db.AvisosAusencia.Add(aviso);
        await db.SaveChangesAsync(ct);
        await AvisarRecorridosAsync(idEstudiante, fecha, ct);
        return (await ListarAsync(db.AvisosAusencia.Where(a => a.Id == aviso.Id), ct))[0];
    }

    /// <summary>El acudiente retira su aviso (no si ya lo confirmaron o es de un día pasado).</summary>
    public async Task CancelarAsync(Guid id, CancellationToken ct = default)
    {
        var idAcudiente = IdAcudiente;
        var estudiantes = db.EstudiantesAcudientes.Where(x => x.IdAcudiente == idAcudiente).Select(x => x.IdEstudiante);
        var aviso = await db.AvisosAusencia.FirstOrDefaultAsync(a => a.Id == id && estudiantes.Contains(a.IdEstudiante), ct)
            ?? throw ErrorNegocio.NoEncontrado("El aviso");
        if (aviso.IdConfirmadoPor is not null)
        {
            throw new ErrorNegocio("La ruta ya confirmó este aviso. Si sí viaja, llama a la monitora o al conductor.", CodigoError.Validacion);
        }
        if (aviso.Fecha < Fechas.Hoy(reloj))
        {
            throw new ErrorNegocio("Ese aviso es de un día que ya pasó.", CodigoError.Validacion);
        }
        db.AvisosAusencia.Remove(aviso);
        await db.SaveChangesAsync(ct);
        await AvisarRecorridosAsync(aviso.IdEstudiante, aviso.Fecha, ct);
    }

    /// <summary>Avisos que aplican a los estudiantes de un recorrido (misma fecha; trayecto igual o ambos).</summary>
    internal static async Task<IReadOnlyList<AvisoAusencia>> DeRecorridosAsync(IBaseDatos db, IReadOnlyCollection<ViajeRuta> viajes, CancellationToken ct)
    {
        if (viajes.Count == 0) return [];
        var fechas = viajes.Select(v => v.FechaViaje).Distinct().ToList();
        var estudiantes = viajes.SelectMany(v => v.Paradas.Select(p => p.IdEstudiante)).Distinct().ToList();
        return await db.AvisosAusencia.AsNoTracking().Include(a => a.Acudiente)
            .Where(a => fechas.Contains(a.Fecha) && estudiantes.Contains(a.IdEstudiante)).ToListAsync(ct);
    }

    internal static bool Aplica(AvisoAusencia a, Guid idEstudiante, DateOnly fecha, Trayecto trayecto) =>
        a.IdEstudiante == idEstudiante && a.Fecha == fecha && (a.Trayecto is null || a.Trayecto == trayecto);

    /// <summary>
    /// El recorrido EN_CURSO del estudiante en esa fecha se marca como modificado y se avisa al conductor y la monitora,
    /// que ven el aviso al instante.
    /// </summary>
    private async Task AvisarRecorridosAsync(Guid idEstudiante, DateOnly fecha, CancellationToken ct)
    {
        if (contexto.IdEmpresa is not { } empresa) return;
        var viajes = await db.ViajesRuta.Where(v => v.FechaViaje == fecha && v.Estado == EstadoViaje.EnCurso
            && v.Paradas.Any(p => p.IdEstudiante == idEstudiante)).ToListAsync(ct);
        if (viajes.Count == 0) return;
        var ahora = reloj.GetUtcNow().UtcDateTime;
        foreach (var v in viajes)
        {
            v.FechaActualizacion = ahora;
        }
        await db.SaveChangesAsync(ct);
        foreach (var v in viajes)
        {
            Guid[] empleados = v.IdMonitora is { } monitora ? [v.IdConductor, monitora] : [v.IdConductor];
            await avisos.RecorridoActualizadoAsync(empresa, new AvisoRecorrido(v.Id, ahora, v.Estado, empleados, []), ct);
        }
    }

    private async Task<IReadOnlyList<AvisoAusenciaDto>> ListarAsync(IQueryable<AvisoAusencia> consulta, CancellationToken ct)
    {
        var lista = await consulta.AsNoTracking().Include(a => a.Estudiante).Include(a => a.Acudiente)
            .OrderBy(a => a.Fecha).ThenBy(a => a.Estudiante.PrimerApellido).ThenBy(a => a.Estudiante.PrimerNombre).ToListAsync(ct);
        var ids = lista.Select(a => a.IdEstudiante).Distinct().ToList();
        var rutas = await db.RutasEstudiantes.AsNoTracking().Include(r => r.Ruta)
            .Where(r => ids.Contains(r.IdEstudiante) && r.Activo && r.Ruta.Activo).ToListAsync(ct);
        return lista.Select(a => new AvisoAusenciaDto(
            a.Id, a.IdEstudiante, a.Estudiante.NombreCompleto, a.Fecha, a.Trayecto is { } t ? TextoEnum.Escribir(t) : "", a.Motivo, a.Nota ?? "",
            a.Acudiente.NombreCompleto, a.Acudiente.Telefono,
            rutas.FirstOrDefault(r => r.IdEstudiante == a.IdEstudiante) is { } r ? ServicioRutas.Etiquetar(r.Ruta) : "",
            a.IdConfirmadoPor is not null, a.NombreConfirmadoPor ?? "", a.FechaConfirmacion, a.FechaCreacion)).ToList();
    }
}
