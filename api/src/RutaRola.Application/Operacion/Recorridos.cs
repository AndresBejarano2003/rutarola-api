using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Domain.Enums;
using RutaRola.Domain.Operacion;

namespace RutaRola.Application.Operacion;

/// <summary>
/// Estudiante del recorrido, con los datos como estaban al iniciar (<c>Parada</c> del frontend).
/// <c>motivoAusencia</c>: <c>NO_ASISTE</c> | <c>POR_SU_CUENTA</c> | <c>""</c>.
/// </summary>
public record ParadaDto(
    Guid EstudianteId, string Estudiante, string Grado, string Direccion, string Acudiente, string TelefonoAcudiente,
    EstadoParada Estado, DateTime? HoraAbordo, DateTime? HoraEntrega, string MotivoAusencia);

/// <summary>Foto de perfil actual de un estudiante del recorrido (solo los que tienen foto).</summary>
public record FotoEstudianteDto(Guid EstudianteId, string Foto);

public record CambioOrdenDto(Guid EmpleadoId, string Nombre, DateTime Hora);

/// <summary><c>Recorrido</c> del frontend (tabla <c>viajes_ruta</c>). <c>paradas</c> en el orden de visita.</summary>
public record RecorridoDto(
    Guid Id, Guid AsignacionId, Guid RutaId, DateOnly Fecha, Trayecto Trayecto, EstadoViaje Estado, string Ruta, string Jornada,
    string Colegio, string DireccionColegio, string Vehiculo, Guid ConductorId, string Conductor, string MonitoraId, string Monitora,
    IReadOnlyList<ParadaDto> Paradas, DateTime? Inicio, DateTime? Fin, string IniciadoPor, CambioOrdenDto? OrdenActualizado,
    string Observaciones, DateTime FechaCreacion, DateTime? FechaActualizacion);

public record ConteoParadasDto(int Total, int Pendientes, int ABordo, int Entregados, int Ausentes);

public record ResumenRecorridoDto(Guid Id, Trayecto Trayecto, EstadoViaje Estado, DateTime? Inicio, DateTime? Fin, ConteoParadasDto Conteo);

public record CompaneroDto(string Nombre, string Telefono, TipoEmpleado Rol, string Foto = "");

/// <summary>Ruta que el empleado autenticado cubre en la fecha (<c>RutaAsignada</c>).</summary>
public record RutaAsignadaDto(
    Guid AsignacionId, Guid RutaId, string Ruta, string Jornada, string Sector, string Colegio, string DireccionColegio,
    string Vehiculo, TipoEmpleado Rol, CompaneroDto? Companero, int Estudiantes, IReadOnlyList<ResumenRecorridoDto> Recorridos);

public record SolicitudIniciarRecorrido(Guid? AsignacionId, Trayecto? Trayecto);

/// <summary><c>motivoAusencia</c> solo con <c>estado</c> AUSENTE (NO_ASISTE solo en la recogida).</summary>
public record SolicitudMarcarParadas(IReadOnlyList<Guid>? EstudianteIds, EstadoParada? Estado, MotivoAusencia? MotivoAusencia = null);

public record SolicitudOrdenParadas(IReadOnlyList<Guid>? EstudianteIds);

public record SolicitudFinalizarRecorrido(string? Observaciones);

/// <summary>Historial del administrador. <c>novedad</c>: AUSENCIAS | OBSERVACIONES.</summary>
public record FiltrosRecorrido(
    string? Texto = null, DateOnly? Desde = null, DateOnly? Hasta = null, Guid? RutaId = null, string? Trayecto = null,
    string? Estado = null, string? Novedad = null);

public class ValidadorIniciarRecorrido : AbstractValidator<SolicitudIniciarRecorrido>
{
    public ValidadorIniciarRecorrido()
    {
        RuleFor(x => x.AsignacionId).NotEmpty().WithMessage("Falta la asignación.");
        RuleFor(x => x.Trayecto).NotNull().WithMessage("Indica si es recogida o entrega.");
    }
}

public class ValidadorMarcarParadas : AbstractValidator<SolicitudMarcarParadas>
{
    public ValidadorMarcarParadas()
    {
        RuleFor(x => x.EstudianteIds).NotEmpty().WithMessage("Indica a qué estudiantes aplica el cambio.");
        RuleFor(x => x.Estado).NotNull().WithMessage("Indica el nuevo estado.");
        RuleFor(x => x.MotivoAusencia).Null().When(x => x.Estado != EstadoParada.Ausente)
            .WithMessage("El motivo solo aplica cuando el estudiante no viaja.");
    }
}

public class ValidadorOrdenParadas : AbstractValidator<SolicitudOrdenParadas>
{
    public ValidadorOrdenParadas() =>
        RuleFor(x => x.EstudianteIds).NotNull().WithMessage("Envía el nuevo orden de los estudiantes.");
}

public class ValidadorFinalizarRecorrido : AbstractValidator<SolicitudFinalizarRecorrido>
{
    public ValidadorFinalizarRecorrido() =>
        RuleFor(x => x.Observaciones).MaximumLength(1000).WithMessage("Las observaciones admiten máximo 1000 caracteres.");
}

/// <summary>
/// Recorridos de la Ruta del día (las mismas reglas de <c>RecorridosLocal</c> y <c>recorrido.ts</c>).
/// El empleado sale SIEMPRE del token: un conductor o monitora solo ve y modifica los recorridos de sus
/// asignaciones (datos de menores). El conductor y la monitora comparten el mismo recorrido.
/// </summary>
public class ServicioRecorridos(IBaseDatos db, IContextoUsuario contexto, IAvisosRecorrido avisos, TimeProvider reloj)
{
    private static readonly Dictionary<EstadoParada, EstadoParada[]> Transiciones = new()
    {
        [EstadoParada.Pendiente] = [EstadoParada.ABordo, EstadoParada.Ausente],
        [EstadoParada.ABordo] = [EstadoParada.Entregado, EstadoParada.Pendiente],
        [EstadoParada.Entregado] = [EstadoParada.ABordo],
        [EstadoParada.Ausente] = [EstadoParada.Pendiente],
    };

    private static string NombreEstado(EstadoParada e) => e switch
    {
        EstadoParada.Pendiente => "Pendiente",
        EstadoParada.ABordo => "A bordo",
        EstadoParada.Entregado => "Entregado",
        _ => "No asistió",
    };

    /// <summary>Igual que <c>nombreAusencia</c> del frontend (<c>recorrido.ts</c>).</summary>
    public static string NombreAusencia(MotivoAusencia? motivo, Trayecto trayecto) => (motivo, trayecto) switch
    {
        (Domain.Enums.MotivoAusencia.NoAsiste, _) => "No asiste hoy al colegio",
        (Domain.Enums.MotivoAusencia.PorSuCuenta, Trayecto.Recogida) => "Va al colegio por su cuenta",
        (_, Trayecto.Entrega) => "No viaja en la ruta",
        _ => "No asistió",
    };

    private DateTime Ahora => reloj.GetUtcNow().UtcDateTime;

    private Guid IdEmpleado => contexto.IdEmpleado
        ?? throw new ErrorNegocio("Inicia sesión como conductor o monitora para ver tu ruta.", CodigoError.Prohibido);

    // ---------- Ruta del día (conductor / monitora) ----------

    public async Task<IReadOnlyList<RutaAsignadaDto>> RutasDelDiaAsync(DateOnly fecha, CancellationToken ct = default)
    {
        var empleado = IdEmpleado;
        var asignaciones = await db.AsignacionesRuta.AsNoTracking()
            .Include(a => a.Ruta).ThenInclude(r => r.Colegio)
            .Include(a => a.Ruta).ThenInclude(r => r.Estudiantes)
            .Include(a => a.Vehiculo).Include(a => a.Conductor).Include(a => a.Monitora)
            .Where(a => a.Activo && a.Ruta.Activo && a.FechaInicio <= fecha && (a.FechaFin == null || a.FechaFin >= fecha)
                && (a.IdConductor == empleado || a.IdMonitora == empleado))
            .ToListAsync(ct);
        var ids = asignaciones.Select(a => a.Id).ToList();
        var recorridos = await db.ViajesRuta.AsNoTracking().Include(v => v.Paradas)
            .Where(v => ids.Contains(v.IdAsignacion) && v.FechaViaje == fecha).ToListAsync(ct);

        return asignaciones.Select(a =>
            {
                var rol = a.IdConductor == empleado ? TipoEmpleado.Conductor : TipoEmpleado.Monitora;
                var otro = rol == TipoEmpleado.Conductor ? a.Monitora : a.Conductor;
                return new RutaAsignadaDto(
                    a.Id, a.IdRuta, ServicioRutas.Etiquetar(a.Ruta), a.Ruta.Jornada, a.Ruta.Sector ?? "", a.Ruta.Colegio.RazonSocial,
                    a.Ruta.Colegio.Direccion, a.Vehiculo.Placa, rol,
                    otro is null ? null : new CompaneroDto(otro.NombreCompleto, otro.Telefono, otro.TipoEmpleado, otro.Foto ?? ""),
                    a.Ruta.Estudiantes.Count,
                    recorridos.Where(v => v.IdAsignacion == a.Id).OrderBy(v => v.Trayecto).Select(Resumir).ToList());
            })
            .OrderBy(r => Catalogos.OrdenJornada(r.Jornada)).ThenBy(r => r.Ruta, StringComparer.Create(new("es-CO"), false))
            .ToList();
    }

    /// <summary>
    /// Inicia el trayecto de hoy. Si el compañero ya lo inició, devuelve ESE recorrido
    /// (<paramref name="nuevo"/> = false → 200 en vez de 201).
    /// </summary>
    public async Task<(RecorridoDto Recorrido, bool Nuevo)> IniciarAsync(SolicitudIniciarRecorrido s, CancellationToken ct = default)
    {
        var empleado = IdEmpleado;
        var hoy = Fechas.Hoy(reloj);
        var trayecto = s.Trayecto!.Value;
        var asignacion = await db.AsignacionesRuta
            .Include(a => a.Ruta).ThenInclude(r => r.Colegio)
            .Include(a => a.Ruta).ThenInclude(r => r.Estudiantes)
            .Include(a => a.Vehiculo).Include(a => a.Conductor).Include(a => a.Monitora)
            .FirstOrDefaultAsync(a => a.Id == s.AsignacionId && a.Activo && a.Ruta.Activo && a.FechaInicio <= hoy
                && (a.FechaFin == null || a.FechaFin >= hoy) && (a.IdConductor == empleado || a.IdMonitora == empleado), ct)
            ?? throw new ErrorNegocio("Esta ruta no está asignada a ti para hoy. Consulta con el administrador.", CodigoError.Validacion);
        var ruta = ServicioRutas.Etiquetar(asignacion.Ruta);

        if (await BuscarDeHoyAsync(asignacion.Id, hoy, trayecto, ct) is { } existente)
        {
            return (existente, false);
        }
        if (await db.ViajesRuta.AnyAsync(v => v.IdAsignacion == asignacion.Id && v.FechaViaje == hoy && v.Estado == EstadoViaje.EnCurso, ct))
        {
            throw new ErrorNegocio($"Hay un recorrido en curso en {ruta}. Finalízalo antes de iniciar otro.", CodigoError.Duplicado);
        }

        var orden = asignacion.Ruta.Estudiantes.OrderBy(e => e.OrdenRecogida).Select(e => e.IdEstudiante).ToList();
        var estudiantes = await db.Estudiantes.AsNoTracking()
            .Include(e => e.Acudientes).ThenInclude(x => x.Acudiente)
            .Where(e => orden.Contains(e.Id) && e.Activo).ToListAsync(ct);
        var enRuta = orden.Select(id => estudiantes.FirstOrDefault(e => e.Id == id)).OfType<Domain.Personas.Estudiante>().ToList();
        if (enRuta.Count == 0)
        {
            throw new ErrorNegocio($"{ruta} no tiene estudiantes activos.", CodigoError.Validacion);
        }
        if (trayecto == Trayecto.Entrega)
        {
            enRuta.Reverse(); // La entrega recorre la ruta al revés.
        }

        // Quien en la recogida de hoy quedó como "No asiste hoy" no está en el colegio: la entrega lo trae como ausente
        // (se puede deshacer si sí llegó).
        var noAsisten = trayecto == Trayecto.Entrega
            ? (await db.ViajesRuta.AsNoTracking()
                .Where(v => v.IdAsignacion == asignacion.Id && v.FechaViaje == hoy && v.Trayecto == Trayecto.Recogida)
                .SelectMany(v => v.Paradas)
                .Where(p => p.Estado == EstadoParada.Ausente && p.MotivoAusencia == Domain.Enums.MotivoAusencia.NoAsiste)
                .Select(p => p.IdEstudiante).ToListAsync(ct)).ToHashSet()
            : [];

        var ahora = Ahora;
        var viaje = new ViajeRuta
        {
            IdRuta = asignacion.IdRuta,
            IdAsignacion = asignacion.Id,
            FechaViaje = hoy,
            Trayecto = trayecto,
            IdConductor = asignacion.IdConductor,
            IdMonitora = asignacion.IdMonitora,
            IdVehiculo = asignacion.IdVehiculo,
            HoraInicio = ahora,
            Estado = EstadoViaje.EnCurso,
            IdIniciadoPor = empleado,
            NombreRuta = ruta,
            Jornada = asignacion.Ruta.Jornada,
            NombreColegio = asignacion.Ruta.Colegio.RazonSocial,
            DireccionColegio = asignacion.Ruta.Colegio.Direccion,
            DescripcionVehiculo = asignacion.Vehiculo.Placa,
            NombreConductor = asignacion.Conductor.NombreCompleto,
            NombreMonitora = asignacion.Monitora?.NombreCompleto,
            Paradas = enRuta.Select((e, i) =>
            {
                var acudiente = (e.Acudientes.FirstOrDefault(x => x.EsPrincipal) ?? e.Acudientes.FirstOrDefault())?.Acudiente;
                return new ParadaViaje
                {
                    IdEstudiante = e.Id,
                    OrdenRecogida = i + 1,
                    NombreEstudiante = e.NombreCompleto,
                    Grado = Catalogos.NombreGrado(e.Grado),
                    Direccion = e.Direccion,
                    NombreAcudiente = acudiente?.NombreCompleto,
                    TelefonoAcudiente = acudiente?.Telefono,
                    Estado = noAsisten.Contains(e.Id) ? EstadoParada.Ausente : EstadoParada.Pendiente,
                    MotivoAusencia = noAsisten.Contains(e.Id) ? Domain.Enums.MotivoAusencia.NoAsiste : null,
                };
            }).ToList(),
        };
        viaje.Eventos.Add(Evento(TipoEvento.Inicio, $"Inició el trayecto de {(trayecto == Trayecto.Recogida ? "recogida" : "entrega")}."));
        db.ViajesRuta.Add(viaje);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // El compañero lo inició en el mismo instante (índice único): se une a ese.
            db.ViajesRuta.Remove(viaje);
            if (await BuscarDeHoyAsync(asignacion.Id, hoy, trayecto, ct) is { } carrera)
            {
                return (carrera, false);
            }
            throw;
        }
        await AvisarAsync(viaje, ct);
        return (await ObtenerAsync(viaje.Id, ct), true);
    }

    public Task<RecorridoDto> MarcarAsync(Guid id, SolicitudMarcarParadas s, CancellationToken ct = default) =>
        ModificarAsync(id, viaje =>
        {
            var ids = s.EstudianteIds!.ToHashSet();
            var paradas = viaje.Paradas.Where(p => ids.Contains(p.IdEstudiante)).OrderBy(p => p.OrdenRecogida).ToList();
            if (paradas.Count == 0)
            {
                throw new ErrorNegocio("El estudiante no hace parte de este recorrido.", CodigoError.NoEncontrado);
            }
            var estado = s.Estado!.Value;
            var motivo = estado == EstadoParada.Ausente ? s.MotivoAusencia : null;
            if (motivo == Domain.Enums.MotivoAusencia.NoAsiste && viaje.Trayecto != Trayecto.Recogida)
            {
                throw new ErrorNegocio("\"No asiste hoy\" se marca en la recogida. En la entrega usa \"No viaja\".", CodigoError.Validacion);
            }
            var ahora = Ahora;
            // Ya ausente con otro motivo: solo se corrige el motivo.
            foreach (var p in paradas.Where(p => p.Estado == estado && estado == EstadoParada.Ausente && p.MotivoAusencia != motivo))
            {
                p.MotivoAusencia = motivo;
                viaje.Eventos.Add(Evento(TipoEvento.CambioParada, $"{p.NombreEstudiante}: {NombreAusencia(motivo, viaje.Trayecto)}."));
            }
            foreach (var p in paradas.Where(p => p.Estado != estado))
            {
                if (!Transiciones[p.Estado].Contains(estado))
                {
                    throw new ErrorNegocio(
                        $"{p.NombreEstudiante}: no se puede pasar de \"{NombreEstado(p.Estado)}\" a \"{NombreEstado(estado)}\".",
                        CodigoError.Validacion);
                }
                switch (estado)
                {
                    case EstadoParada.ABordo:
                        p.HoraRecogida = p.Estado == EstadoParada.Entregado ? p.HoraRecogida : ahora;
                        p.HoraEntrega = null;
                        break;
                    case EstadoParada.Entregado:
                        p.HoraEntrega = ahora;
                        break;
                    default:
                        p.HoraRecogida = null;
                        p.HoraEntrega = null;
                        break;
                }
                p.Estado = estado;
                p.MotivoAusencia = motivo;
                viaje.Eventos.Add(Evento(TipoEvento.CambioParada,
                    $"{p.NombreEstudiante}: {(estado == EstadoParada.Ausente ? NombreAusencia(motivo, viaje.Trayecto) : NombreEstado(estado))}."));
            }
        }, ct);

    public Task<RecorridoDto> ReordenarAsync(Guid id, SolicitudOrdenParadas s, CancellationToken ct = default) =>
        ModificarAsync(id, viaje =>
        {
            var empleado = IdEmpleado;
            var puede = viaje.IdMonitora is { } monitora ? empleado == monitora : empleado == viaje.IdConductor;
            if (!puede)
            {
                throw new ErrorNegocio(
                    viaje.IdMonitora is null
                        ? "Solo el conductor puede cambiar el orden de los estudiantes."
                        : "Solo la monitora puede cambiar el orden de los estudiantes.",
                    CodigoError.Prohibido);
            }

            // Solo se reordenan las paradas por visitar; las visitadas quedan arriba, en su orden.
            var enOrden = viaje.Paradas.OrderBy(p => p.OrdenRecogida).ToList();
            var visitadas = enOrden.Where(p => !PorVisitar(p, viaje.Trayecto)).ToList();
            var pendientes = enOrden.Where(p => PorVisitar(p, viaje.Trayecto)).ToDictionary(p => p.IdEstudiante);
            var ids = s.EstudianteIds!;
            if (ids.Count != pendientes.Count || ids.Distinct().Count() != ids.Count || ids.Any(x => !pendientes.ContainsKey(x)))
            {
                throw new ErrorNegocio("El recorrido cambió mientras ordenabas. Revisa la lista e inténtalo de nuevo.", CodigoError.Conflicto);
            }
            var nuevo = visitadas.Concat(ids.Select(x => pendientes[x])).ToList();
            for (var i = 0; i < nuevo.Count; i++)
            {
                nuevo[i].OrdenRecogida = i + 1;
            }

            var nombre = empleado == viaje.IdConductor ? viaje.NombreConductor : viaje.NombreMonitora ?? "";
            viaje.IdOrdenActualizadoPor = empleado;
            viaje.NombreOrdenActualizadoPor = nombre;
            viaje.FechaOrdenActualizado = Ahora;
            viaje.Eventos.Add(Evento(TipoEvento.Reorden, $"{nombre} cambió el orden de visita."));
        }, ct);

    public Task<RecorridoDto> FinalizarAsync(Guid id, SolicitudFinalizarRecorrido s, CancellationToken ct = default) =>
        ModificarAsync(id, viaje =>
        {
            var aBordo = viaje.Paradas.Count(p => p.Estado == EstadoParada.ABordo);
            var pendientes = viaje.Paradas.Count(p => p.Estado == EstadoParada.Pendiente);
            if (aBordo > 0)
            {
                throw new ErrorNegocio(
                    $"{(aBordo == 1 ? "Hay 1 estudiante" : $"Hay {aBordo} estudiantes")} a bordo. Ningún estudiante puede quedar en el vehículo: márcalo como entregado.",
                    CodigoError.Validacion);
            }
            if (pendientes > 0)
            {
                throw new ErrorNegocio(
                    $"{(pendientes == 1 ? "Falta 1 estudiante" : $"Faltan {pendientes} estudiantes")} por marcar como recogido o \"No asistió\".",
                    CodigoError.Validacion);
            }
            viaje.Estado = EstadoViaje.Finalizado;
            viaje.HoraFin = Ahora;
            viaje.Observaciones = Texto.Opcional(s.Observaciones);
            viaje.Eventos.Add(Evento(TipoEvento.Finalizacion, "Finalizó el recorrido."));
        }, ct);

    // ---------- Consulta (empleado de la asignación o administrador) ----------

    /// <summary>
    /// Con <paramref name="todaLaEmpresa"/> (administrador) cualquier recorrido de la empresa; si no, solo
    /// los del empleado del token (403 si es de otra asignación).
    /// </summary>
    public async Task<RecorridoDto> ObtenerAsync(Guid id, bool todaLaEmpresa, CancellationToken ct = default)
    {
        var viaje = await db.ViajesRuta.AsNoTracking().Include(v => v.Paradas).FirstOrDefaultAsync(v => v.Id == id, ct)
            ?? throw new ErrorNegocio("El recorrido no existe o no pertenece a tus rutas.", CodigoError.NoEncontrado);
        if (!todaLaEmpresa && !EsDelEmpleado(viaje, IdEmpleado))
        {
            throw new ErrorNegocio("El recorrido no pertenece a tus rutas.", CodigoError.Prohibido);
        }
        return Mapear(viaje);
    }

    private Task<RecorridoDto> ObtenerAsync(Guid id, CancellationToken ct) => ObtenerAsync(id, false, ct);

    /// <summary>
    /// Fotos ACTUALES de los estudiantes del recorrido, para que la monitora y el conductor los reconozcan. Van aparte
    /// del recorrido (que se vuelve a pedir en cada marca) para que el teléfono las descargue una sola vez por pantalla.
    /// Mismo acceso que <see cref="ObtenerAsync(Guid, bool, CancellationToken)"/>: son datos de menores.
    /// </summary>
    public async Task<IReadOnlyList<FotoEstudianteDto>> FotosAsync(Guid id, bool todaLaEmpresa, CancellationToken ct = default)
    {
        var viaje = await db.ViajesRuta.AsNoTracking().Include(v => v.Paradas).FirstOrDefaultAsync(v => v.Id == id, ct)
            ?? throw new ErrorNegocio("El recorrido no existe o no pertenece a tus rutas.", CodigoError.NoEncontrado);
        if (!todaLaEmpresa && !EsDelEmpleado(viaje, IdEmpleado))
        {
            throw new ErrorNegocio("El recorrido no pertenece a tus rutas.", CodigoError.Prohibido);
        }
        var ids = viaje.Paradas.Select(p => p.IdEstudiante).ToList();
        return await db.Estudiantes.AsNoTracking().Where(e => ids.Contains(e.Id) && e.Foto != null)
            .Select(e => new FotoEstudianteDto(e.Id, e.Foto!)).ToListAsync(ct);
    }

    /// <summary>Historial del administrador: del más reciente al más antiguo.</summary>
    public async Task<ListaPaginada<RecorridoDto>> ListarAsync(FiltrosRecorrido f, Paginacion paginacion, CancellationToken ct = default)
    {
        var consulta = db.ViajesRuta.AsNoTracking();
        if (Texto.Opcional(f.Texto) is { } texto)
        {
            var busqueda = Busqueda.Normalizar(texto);
            consulta = consulta.Where(v =>
                FuncionesBd.SinTildes(v.NombreRuta + " " + v.NombreColegio + " " + v.DescripcionVehiculo + " " + v.NombreConductor
                    + " " + (v.NombreMonitora ?? "")).Contains(busqueda)
                || v.Paradas.Any(p => FuncionesBd.SinTildes(p.NombreEstudiante).Contains(busqueda)));
        }
        if (f.Desde is { } desde)
        {
            consulta = consulta.Where(v => v.FechaViaje >= desde);
        }
        if (f.Hasta is { } hasta)
        {
            consulta = consulta.Where(v => v.FechaViaje <= hasta);
        }
        if (f.RutaId is { } ruta)
        {
            consulta = consulta.Where(v => v.IdRuta == ruta);
        }
        if (TextoEnum.Leer<Trayecto>(f.Trayecto) is { } trayecto)
        {
            consulta = consulta.Where(v => v.Trayecto == trayecto);
        }
        if (TextoEnum.Leer<EstadoViaje>(f.Estado) is { } estado)
        {
            consulta = consulta.Where(v => v.Estado == estado);
        }
        consulta = Texto.Opcional(f.Novedad) switch
        {
            "AUSENCIAS" => consulta.Where(v => v.Paradas.Any(p => p.Estado == EstadoParada.Ausente)),
            "OBSERVACIONES" => consulta.Where(v => v.Observaciones != null && v.Observaciones != ""),
            _ => consulta,
        };

        var total = await consulta.CountAsync(ct);
        var viajes = await consulta.OrderByDescending(v => v.FechaViaje).ThenByDescending(v => v.HoraInicio).ThenByDescending(v => v.Id)
            .Skip(paginacion.Omitir).Take(paginacion.TamanoValido).Include(v => v.Paradas).ToListAsync(ct);
        return new ListaPaginada<RecorridoDto>(viajes.Select(Mapear).ToList(), total, paginacion.PaginaValida, paginacion.TamanoValido);
    }

    // ---------- Ayudas ----------

    private async Task<RecorridoDto> ModificarAsync(Guid id, Action<ViajeRuta> cambio, CancellationToken ct)
    {
        var empleado = IdEmpleado;
        var viaje = await db.ViajesRuta.Include(v => v.Paradas).FirstOrDefaultAsync(v => v.Id == id, ct);
        if (viaje is null || !EsDelEmpleado(viaje, empleado))
        {
            throw new ErrorNegocio("El recorrido no existe o no pertenece a tus rutas.", CodigoError.NoEncontrado);
        }
        if (viaje.Estado != EstadoViaje.EnCurso)
        {
            throw new ErrorNegocio("Este recorrido ya fue finalizado y no se puede modificar.", CodigoError.Validacion);
        }
        cambio(viaje);
        // Marca el recorrido como modificado aunque solo cambien sus paradas (el compañero lo detecta por la fecha).
        viaje.FechaActualizacion = Ahora;
        await db.SaveChangesAsync(ct);
        await AvisarAsync(viaje, ct);
        return await ObtenerAsync(id, ct);
    }

    /// <summary>Aviso en tiempo real al compañero, al administrador y a los acudientes de los estudiantes del recorrido.</summary>
    private async Task AvisarAsync(ViajeRuta viaje, CancellationToken ct)
    {
        if (contexto.IdEmpresa is not { } empresa)
        {
            return;
        }
        var estudiantes = viaje.Paradas.Select(p => p.IdEstudiante).ToList();
        var acudientes = await db.EstudiantesAcudientes.AsNoTracking().Where(x => estudiantes.Contains(x.IdEstudiante))
            .Select(x => x.IdAcudiente).Distinct().ToListAsync(ct);
        Guid[] empleados = viaje.IdMonitora is { } monitora ? [viaje.IdConductor, monitora] : [viaje.IdConductor];
        await avisos.RecorridoActualizadoAsync(
            empresa, new AvisoRecorrido(viaje.Id, viaje.FechaActualizacion ?? viaje.FechaCreacion, viaje.Estado, empleados, acudientes), ct);
    }

    private async Task<RecorridoDto?> BuscarDeHoyAsync(Guid idAsignacion, DateOnly fecha, Trayecto trayecto, CancellationToken ct)
    {
        var viaje = await db.ViajesRuta.AsNoTracking().Include(v => v.Paradas)
            .FirstOrDefaultAsync(v => v.IdAsignacion == idAsignacion && v.FechaViaje == fecha && v.Trayecto == trayecto, ct);
        return viaje is null ? null : Mapear(viaje);
    }

    private static bool EsDelEmpleado(ViajeRuta v, Guid empleado) => v.IdConductor == empleado || v.IdMonitora == empleado;

    /// <summary>RECOGIDA: faltan las PENDIENTES; ENTREGA: las PENDIENTES y las A BORDO (aún no llegan a su casa).</summary>
    private static bool PorVisitar(ParadaViaje p, Trayecto trayecto) =>
        p.Estado == EstadoParada.Pendiente || (trayecto == Trayecto.Entrega && p.Estado == EstadoParada.ABordo);

    private Evento Evento(TipoEvento tipo, string descripcion) =>
        new() { TipoEvento = tipo, Descripcion = descripcion, IdUsuario = contexto.IdUsuario, FechaEvento = Ahora };

    /// <summary>Igual que el JSON de los enums (<c>NoAsiste</c> → <c>NO_ASISTE</c>); sin motivo = <c>""</c>.</summary>
    public static string TextoMotivo(MotivoAusencia? motivo) => motivo switch
    {
        Domain.Enums.MotivoAusencia.NoAsiste => "NO_ASISTE",
        Domain.Enums.MotivoAusencia.PorSuCuenta => "POR_SU_CUENTA",
        _ => "",
    };

    private static ResumenRecorridoDto Resumir(ViajeRuta v) => new(v.Id, v.Trayecto, v.Estado, v.HoraInicio, v.HoraFin, Contar(v.Paradas));

    private static ConteoParadasDto Contar(IReadOnlyCollection<ParadaViaje> paradas) => new(
        paradas.Count,
        paradas.Count(p => p.Estado == EstadoParada.Pendiente),
        paradas.Count(p => p.Estado == EstadoParada.ABordo),
        paradas.Count(p => p.Estado == EstadoParada.Entregado),
        paradas.Count(p => p.Estado == EstadoParada.Ausente));

    private static RecorridoDto Mapear(ViajeRuta v) => new(
        v.Id, v.IdAsignacion, v.IdRuta, v.FechaViaje, v.Trayecto, v.Estado, v.NombreRuta, v.Jornada, v.NombreColegio,
        v.DireccionColegio ?? "", v.DescripcionVehiculo, v.IdConductor, v.NombreConductor, v.IdMonitora?.ToString() ?? "",
        v.NombreMonitora ?? "",
        v.Paradas.OrderBy(p => p.OrdenRecogida).Select(p => new ParadaDto(
            p.IdEstudiante, p.NombreEstudiante, p.Grado, p.Direccion, p.NombreAcudiente ?? "", p.TelefonoAcudiente ?? "",
            p.Estado, p.HoraRecogida, p.HoraEntrega, TextoMotivo(p.MotivoAusencia))).ToList(),
        v.HoraInicio, v.HoraFin, v.IdIniciadoPor?.ToString() ?? "",
        v.IdOrdenActualizadoPor is { } quien && v.FechaOrdenActualizado is { } hora
            ? new CambioOrdenDto(quien, v.NombreOrdenActualizadoPor ?? "", hora)
            : null,
        v.Observaciones ?? "", v.FechaCreacion, v.FechaActualizacion);
}
