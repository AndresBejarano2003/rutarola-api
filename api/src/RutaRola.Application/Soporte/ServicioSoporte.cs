using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comunicacion;
using RutaRola.Application.Comun;
using RutaRola.Application.Finanzas;
using RutaRola.Domain.Enums;
using RutaRola.Domain.Seguridad;
using RutaRola.Domain.Soporte;

namespace RutaRola.Application.Soporte;

/// <summary>
/// Tickets de soporte. La EMPRESA (administrador, <c>soporte.solicitar</c>) crea y sigue los suyos: comenta,
/// valida la solución, confirma con una calificación, reabre o cancela; nunca ve las notas internas.
/// La PLATAFORMA (SuperAdmin, <c>soporte.gestionar</c>) ve los de todas las empresas y los lleva por el flujo
/// de <see cref="ReglasTicket"/>: revisión, proceso, QA, pruebas, validación, con responsable, prioridad
/// (SLA), comentarios, notas internas y reuniones. Cada paso queda en el historial (<c>eventos_ticket</c>).
/// </summary>
public class ServicioSoporte(IBaseDatos db, IContextoUsuario contexto, AvisosSoporte avisos, TimeProvider reloj)
{
    private DateTime Ahora => reloj.GetUtcNow().UtcDateTime;

    // --- Empresa ---------------------------------------------------------------------------------

    public async Task<TicketDto> CrearAsync(SolicitudTicket s, CancellationToken ct = default)
    {
        var idUsuario = contexto.IdUsuario ?? throw new ErrorNegocio("Inicia sesión de nuevo.", CodigoError.Prohibido);
        var usuario = await db.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.Id == idUsuario, ct)
            ?? throw new ErrorNegocio("Inicia sesión de nuevo.", CodigoError.Prohibido);
        var ahora = Ahora;
        var adjuntos = Decodificar(s.Adjuntos);
        var ticket = new TicketSoporte
        {
            Numero = await Consecutivos.SiguienteAsync(db.TicketsSoporte.IgnoreQueryFilters([FiltrosConsulta.Empresa]).Select(t => t.Numero), "SOP", ct),
            Tipo = s.Tipo!.Value,
            Modulo = s.Modulo!.Trim(),
            Prioridad = s.Prioridad!.Value,
            Asunto = s.Asunto!.Trim(),
            Descripcion = s.Descripcion!.Trim(),
            IdSolicitante = usuario.Id,
            NombreSolicitante = usuario.Nombre,
            CorreoSolicitante = Texto.Opcional(usuario.Email),
            FechaObjetivo = ahora + ReglasTicket.Objetivo(s.Prioridad.Value),
            FechaUltimaActividad = ahora,
        };
        var evento = Evento(ticket, TipoEventoTicket.Creado, null, EstadoTicket.Nuevo, null, interno: false, plataforma: false, usuario.Nombre);
        Adjuntar(ticket, evento, adjuntos, ahora);
        db.TicketsSoporte.Add(ticket);
        await db.SaveChangesAsync(ct);

        await avisos.CreadoAsync(ticket, await NombreEmpresaAsync(ticket.IdEmpresa, ct),
            adjuntos.Select(a => new AdjuntoCorreo(a.Nombre, a.Tipo, a.Contenido)).ToList(), ct);
        return await ObtenerAsync(ticket.Id, plataforma: false, ct);
    }

    public Task<TicketDto> ComentarAsync(Guid id, SolicitudComentarioTicket s, bool plataforma, CancellationToken ct = default) =>
        ModificarAsync(id, plataforma, (t, nombre) =>
        {
            var interno = plataforma && s.Interno;
            if (!plataforma && ReglasTicket.Finalizados.Contains(t.Estado))
            {
                throw new ErrorNegocio("El ticket ya terminó. Si necesitas algo más, abre una nueva solicitud.", CodigoError.Validacion);
            }
            var evento = Evento(t, interno ? TipoEventoTicket.NotaInterna : TipoEventoTicket.Comentario, null, null, s.Texto!.Trim(), interno, plataforma, nombre);
            Adjuntar(t, evento, Decodificar(s.Adjuntos), Ahora);
            // La empresa respondió lo que se le pidió: vuelve a revisión.
            if (!plataforma && t.Estado == EstadoTicket.EsperandoCliente)
            {
                Evento(t, TipoEventoTicket.CambioEstado, t.Estado, EstadoTicket.EnRevision, "La empresa respondió.", false, false, nombre);
                t.Estado = EstadoTicket.EnRevision;
            }
            return interno ? null : plataforma
                ? new Aviso(true, "Tienes una respuesta del equipo de soporte", s.Texto.Trim())
                : new Aviso(false, "Nuevo comentario", s.Texto.Trim());
        }, ct);

    public Task<TicketDto> ValidarAsync(Guid id, SolicitudValidacionTicket s, CancellationToken ct = default) =>
        ModificarAsync(id, plataforma: false, (t, nombre) =>
        {
            if (t.Estado != EstadoTicket.ValidacionCliente)
            {
                throw new ErrorNegocio("Este ticket no está pendiente de tu validación.", CodigoError.Validacion);
            }
            var aprobada = s.Aprobada!.Value;
            var texto = Texto.Opcional(s.Comentario) ?? "La solución funciona correctamente.";
            Cambiar(t, aprobada ? EstadoTicket.Resuelto : EstadoTicket.EnProceso, texto, plataforma: false, nombre);
            return new Aviso(false, aprobada ? "La empresa aprobó la solución" : "La empresa rechazó la solución", texto);
        }, ct);

    public Task<TicketDto> ConfirmarAsync(Guid id, SolicitudConfirmacionTicket s, CancellationToken ct = default) =>
        ModificarAsync(id, plataforma: false, (t, nombre) =>
        {
            if (t.Estado != EstadoTicket.Resuelto)
            {
                throw new ErrorNegocio("Solo se confirma un ticket resuelto.", CodigoError.Validacion);
            }
            t.Calificacion = s.Calificacion;
            t.ComentarioCalificacion = Texto.Opcional(s.Comentario);
            Evento(t, TipoEventoTicket.Calificacion, null, null, $"Calificación: {s.Calificacion}/5{(t.ComentarioCalificacion is { } c ? $". {c}" : "")}", false, false, nombre);
            Cambiar(t, EstadoTicket.Cerrado, "La empresa confirmó la solución.", plataforma: false, nombre);
            return new Aviso(false, $"Ticket cerrado con {s.Calificacion}/5", t.ComentarioCalificacion);
        }, ct);

    public Task<TicketDto> ReabrirAsync(Guid id, SolicitudMotivoTicket s, CancellationToken ct = default) =>
        ModificarAsync(id, plataforma: false, (t, nombre) =>
        {
            if (t.Estado != EstadoTicket.Resuelto)
            {
                throw new ErrorNegocio("Solo se reabre un ticket resuelto.", CodigoError.Validacion);
            }
            Cambiar(t, EstadoTicket.EnRevision, s.Comentario!.Trim(), plataforma: false, nombre);
            return new Aviso(false, "La empresa reabrió el ticket", s.Comentario.Trim());
        }, ct);

    public Task<TicketDto> CancelarAsync(Guid id, SolicitudMotivoTicket s, CancellationToken ct = default) =>
        ModificarAsync(id, plataforma: false, (t, nombre) =>
        {
            if (!ReglasTicket.AccionesCliente(t.Estado).Cancelar)
            {
                throw new ErrorNegocio("Este ticket ya no se puede cancelar.", CodigoError.Validacion);
            }
            Cambiar(t, EstadoTicket.Cancelado, s.Comentario!.Trim(), plataforma: false, nombre);
            return new Aviso(false, "La empresa canceló el ticket", s.Comentario.Trim());
        }, ct);

    // --- Plataforma ------------------------------------------------------------------------------

    public Task<TicketDto> CambiarEstadoAsync(Guid id, SolicitudEstadoTicket s, CancellationToken ct = default) =>
        ModificarAsync(id, plataforma: true, (t, nombre) =>
        {
            var hacia = s.Estado!.Value;
            if (!ReglasTicket.Siguientes(t.Estado, t.Tipo).Contains(hacia))
            {
                throw new ErrorNegocio(
                    ReglasTicket.RequiereCalidad(t.Tipo) && hacia == EstadoTicket.Resuelto
                        ? $"Un ticket de tipo {ReglasTicket.Nombre(t.Tipo).ToLowerInvariant()} debe pasar por QA, pruebas y la validación del cliente antes de resolverse."
                        : $"No se puede pasar de \"{ReglasTicket.Nombre(t.Estado)}\" a \"{ReglasTicket.Nombre(hacia)}\".",
                    CodigoError.Validacion);
            }
            var comentario = Texto.Opcional(s.Comentario);
            if (comentario is null && ReglasTicket.RequiereComentario(t.Estado, hacia))
            {
                throw new ErrorNegocio("Explica el motivo del cambio: la empresa lo verá en el historial.", CodigoError.Validacion);
            }
            Cambiar(t, hacia, comentario, plataforma: true, nombre);
            return new Aviso(true, hacia switch
            {
                EstadoTicket.EsperandoCliente => "Necesitamos información para continuar",
                EstadoTicket.ValidacionCliente => "Valida la solución, por favor",
                EstadoTicket.Resuelto => "Tu solicitud fue resuelta",
                EstadoTicket.Rechazado => "Tu solicitud no procede",
                _ => $"Tu solicitud pasó a: {ReglasTicket.Nombre(hacia)}",
            }, comentario ?? "");
        }, ct);

    public async Task<TicketDto> AsignarAsync(Guid id, SolicitudAsignacionTicket s, CancellationToken ct = default)
    {
        Guid? responsable = string.IsNullOrEmpty(s.ResponsableId) ? null : Guid.Parse(s.ResponsableId);
        var usuario = responsable is { } r
            ? await db.Usuarios.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == r && u.Activo && u.Rol.Nombre == Roles.SuperAdmin, ct)
                ?? throw ErrorNegocio.NoEncontrado("El responsable")
            : null;
        return await ModificarAsync(id, plataforma: true, (t, nombre) =>
        {
            if (usuario?.Id != t.IdResponsable)
            {
                t.IdResponsable = usuario?.Id;
                t.NombreResponsable = usuario?.Nombre;
                Evento(t, TipoEventoTicket.Asignacion, null, null, usuario is null ? "Quedó sin responsable." : $"Responsable: {usuario.Nombre}.", false, true, nombre);
            }
            var prioridad = s.Prioridad!.Value;
            if (prioridad != t.Prioridad)
            {
                Evento(t, TipoEventoTicket.CambioPrioridad, null, null,
                    $"Prioridad: {ReglasTicket.Nombre(t.Prioridad)} → {ReglasTicket.Nombre(prioridad)}.", false, true, nombre);
                t.Prioridad = prioridad;
                t.FechaObjetivo = t.FechaCreacion + ReglasTicket.Objetivo(prioridad);
            }
            return null;
        }, ct);
    }

    public Task<TicketDto> ProgramarReunionAsync(Guid id, SolicitudReunionTicket s, CancellationToken ct = default) =>
        ModificarAsync(id, plataforma: true, (t, nombre) =>
        {
            if (ReglasTicket.Finalizados.Contains(t.Estado))
            {
                throw new ErrorNegocio("El ticket ya terminó: no se agendan reuniones.", CodigoError.Validacion);
            }
            var fecha = DateTime.SpecifyKind(s.Fecha!.Value.ToUniversalTime(), DateTimeKind.Utc);
            if (fecha < Ahora.AddMinutes(-5))
            {
                throw new ErrorNegocio("La reunión debe ser en una fecha futura.", CodigoError.Validacion);
            }
            var reunion = new ReunionTicket
            {
                IdTicket = t.Id, Fecha = fecha, DuracionMinutos = s.DuracionMinutos!.Value, Modalidad = s.Modalidad!.Value,
                Enlace = Texto.Opcional(s.Enlace), Motivo = s.Motivo!.Trim(), NombreOrganizador = nombre, FechaCreacion = Ahora,
            };
            t.Reuniones.Add(reunion);
            var detalle = $"{Modalidad(reunion.Modalidad)} el {fecha:yyyy-MM-dd HH:mm} UTC ({reunion.DuracionMinutos} min): {reunion.Motivo}"
                + (reunion.Enlace is { } e ? $" · {e}" : "");
            Evento(t, TipoEventoTicket.Reunion, null, null, $"Reunión programada: {detalle}", false, true, nombre);
            return new Aviso(true, "Te agendamos una reunión", detalle);
        }, ct);

    public Task<TicketDto> ActualizarReunionAsync(Guid id, Guid idReunion, SolicitudEstadoReunion s, CancellationToken ct = default) =>
        ModificarAsync(id, plataforma: true, (t, nombre) =>
        {
            var reunion = t.Reuniones.FirstOrDefault(r => r.Id == idReunion) ?? throw ErrorNegocio.NoEncontrado("La reunión");
            if (reunion.Estado != EstadoReunion.Programada)
            {
                throw new ErrorNegocio("Esta reunión ya se registró.", CodigoError.Validacion);
            }
            reunion.Estado = s.Estado!.Value;
            reunion.Notas = s.Notas!.Trim();
            var realizada = reunion.Estado == EstadoReunion.Realizada;
            Evento(t, TipoEventoTicket.Reunion, null, null,
                $"{(realizada ? "Reunión realizada" : "Reunión cancelada")} ({reunion.Fecha:yyyy-MM-dd HH:mm} UTC): {reunion.Notas}", false, true, nombre);
            return new Aviso(true, realizada ? "Resumen de la reunión" : "Se canceló la reunión", reunion.Notas);
        }, ct);

    public async Task<ResumenTicketsDto> ResumenAsync(CancellationToken ct = default)
    {
        var ahora = Ahora;
        var tickets = Consulta(plataforma: true);
        var porEstado = await tickets.GroupBy(t => t.Estado).Select(g => new { g.Key, Total = g.Count() }).ToListAsync(ct);
        var finalizados = ReglasTicket.Finalizados.ToList();
        var sinVencer = ReglasTicket.SinVencimiento.ToList();
        return new ResumenTicketsDto(
            Enum.GetValues<EstadoTicket>().ToDictionary(TextoEstado, e => porEstado.FirstOrDefault(x => x.Key == e)?.Total ?? 0),
            await tickets.CountAsync(t => !finalizados.Contains(t.Estado), ct),
            await tickets.CountAsync(t => !sinVencer.Contains(t.Estado) && t.FechaObjetivo < ahora, ct),
            await tickets.CountAsync(t => !finalizados.Contains(t.Estado) && t.IdResponsable == null, ct),
            await db.ReunionesTicket.IgnoreQueryFilters([FiltrosConsulta.Empresa])
                .CountAsync(r => r.Estado == EstadoReunion.Programada && r.Fecha >= ahora && r.Fecha < ahora.AddDays(7), ct));
    }

    public async Task<IReadOnlyList<ResponsableDto>> ResponsablesAsync(CancellationToken ct = default) =>
        await db.Usuarios.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Activo && u.Rol.Nombre == Roles.SuperAdmin).OrderBy(u => u.Nombre)
            .Select(u => new ResponsableDto(u.Id, u.Nombre)).ToListAsync(ct);

    /// <summary>
    /// Proceso del servidor (modo sistema): cierra los tickets RESUELTOS sin respuesta del cliente en
    /// <see cref="ReglasTicket.DiasCierreAutomatico"/> días. Devuelve cuántos cerró.
    /// </summary>
    public async Task<int> CerrarResueltosAsync(CancellationToken ct = default)
    {
        db.UsarModoSistema();
        var limite = Ahora.AddDays(-ReglasTicket.DiasCierreAutomatico);
        var tickets = await db.TicketsSoporte.IgnoreQueryFilters()
            .Where(t => t.Estado == EstadoTicket.Resuelto && t.FechaResolucion < limite).ToListAsync(ct);
        foreach (var t in tickets)
        {
            Cambiar(t, EstadoTicket.Cerrado, $"Cerrado automáticamente: sin respuesta de la empresa en {ReglasTicket.DiasCierreAutomatico} días.", plataforma: true, "RutaRola");
        }
        if (tickets.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
        return tickets.Count;
    }

    // --- Lectura ---------------------------------------------------------------------------------

    public async Task<ListaPaginada<TicketResumenDto>> ListarAsync(FiltrosTicket f, Paginacion paginacion, bool plataforma, CancellationToken ct = default)
    {
        var ahora = Ahora;
        var consulta = Consulta(plataforma);
        if (Texto.Opcional(f.Texto) is { } texto)
        {
            var busqueda = Busqueda.Normalizar(texto);
            consulta = consulta.Where(t => t.Numero.ToLower().Contains(busqueda) || FuncionesBd.SinTildes(t.Asunto).Contains(busqueda)
                || FuncionesBd.SinTildes(t.NombreSolicitante).Contains(busqueda));
        }
        var finalizados = ReglasTicket.Finalizados.ToList();
        var sinVencer = ReglasTicket.SinVencimiento.ToList();
        consulta = Texto.Opcional(f.Estado) switch
        {
            null => consulta,
            "ABIERTOS" => consulta.Where(t => !finalizados.Contains(t.Estado)),
            "FINALIZADOS" => consulta.Where(t => finalizados.Contains(t.Estado)),
            var e => TextoEnum.Leer<EstadoTicket>(e) is { } estado ? consulta.Where(t => t.Estado == estado) : consulta.Where(_ => false),
        };
        if (TextoEnum.Leer<PrioridadTicket>(f.Prioridad) is { } prioridad) consulta = consulta.Where(t => t.Prioridad == prioridad);
        if (TextoEnum.Leer<TipoTicket>(f.Tipo) is { } tipo) consulta = consulta.Where(t => t.Tipo == tipo);
        if (plataforma && f.EmpresaId is { } empresa) consulta = consulta.Where(t => t.IdEmpresa == empresa);
        if (plataforma && Texto.Opcional(f.ResponsableId) is { } responsable)
        {
            consulta = responsable == "SIN_ASIGNAR"
                ? consulta.Where(t => t.IdResponsable == null)
                : Guid.TryParse(responsable, out var idResponsable) ? consulta.Where(t => t.IdResponsable == idResponsable) : consulta.Where(_ => false);
        }
        if (f.Vencidos == true) consulta = consulta.Where(t => !sinVencer.Contains(t.Estado) && t.FechaObjetivo < ahora);

        var total = await consulta.CountAsync(ct);
        // Abiertos primero (los más urgentes arriba), luego los terminados más recientes.
        var filas = await consulta
            .OrderBy(t => finalizados.Contains(t.Estado))
            // La prioridad se guarda como texto: se ordena por nivel, no alfabéticamente.
            .ThenByDescending(t => t.Prioridad == PrioridadTicket.Critica ? 3 : t.Prioridad == PrioridadTicket.Alta ? 2 : t.Prioridad == PrioridadTicket.Media ? 1 : 0)
            .ThenByDescending(t => t.FechaUltimaActividad)
            .Skip(paginacion.Omitir).Take(paginacion.TamanoValido)
            .Select(t => new
            {
                Ticket = t,
                Reunion = t.Reuniones.Where(r => r.Estado == EstadoReunion.Programada && r.Fecha >= ahora).OrderBy(r => r.Fecha).Select(r => (DateTime?)r.Fecha).FirstOrDefault(),
            })
            .AsNoTracking().ToListAsync(ct);
        var idsEmpresa = filas.Select(x => x.Ticket.IdEmpresa).Distinct().ToList();
        var empresas = await db.Empresas.IgnoreQueryFilters([FiltrosConsulta.Empresa]).Where(e => idsEmpresa.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.Nombre, ct);
        var items = filas.Select(x => new TicketResumenDto(
            x.Ticket.Id, x.Ticket.Numero, x.Ticket.IdEmpresa, empresas.GetValueOrDefault(x.Ticket.IdEmpresa, ""), x.Ticket.Tipo, x.Ticket.Modulo, x.Ticket.Prioridad, x.Ticket.Asunto,
            x.Ticket.Estado, x.Ticket.NombreSolicitante, x.Ticket.IdResponsable?.ToString() ?? "", x.Ticket.NombreResponsable ?? "",
            x.Ticket.FechaCreacion, x.Ticket.FechaUltimaActividad, x.Ticket.FechaObjetivo,
            ReglasTicket.Vencido(x.Ticket.Estado, x.Ticket.FechaObjetivo, ahora), x.Reunion)).ToList();
        return new ListaPaginada<TicketResumenDto>(items, total, paginacion.PaginaValida, paginacion.TamanoValido);
    }

    public async Task<TicketDto> ObtenerAsync(Guid id, bool plataforma, CancellationToken ct = default)
    {
        var t = await Consulta(plataforma).AsNoTracking()
            .Include(x => x.Eventos.Where(e => plataforma || !e.Interno).OrderBy(e => e.Fecha))
            .Include(x => x.Reuniones.OrderBy(r => r.Fecha))
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw ErrorNegocio.NoEncontrado("El ticket");
        var adjuntos = await db.AdjuntosTicket.IgnoreQueryFilters([FiltrosConsulta.Empresa]).AsNoTracking()
            .Where(a => a.IdTicket == id)
            .Select(a => new { a.IdEvento, Dto = new AdjuntoTicketDto(a.Id, a.Nombre, a.Tipo, a.Tamano) })
            .ToListAsync(ct);
        var deEvento = adjuntos.ToLookup(a => a.IdEvento, a => a.Dto);
        var creado = t.Eventos.FirstOrDefault(e => e.Tipo == TipoEventoTicket.Creado);
        var ahora = Ahora;
        return new TicketDto(
            t.Id, t.Numero, t.IdEmpresa, await NombreEmpresaAsync(t.IdEmpresa, ct), t.Tipo, t.Modulo, t.Prioridad, t.Asunto, t.Descripcion,
            t.Estado, t.NombreSolicitante, t.CorreoSolicitante ?? "", t.IdResponsable?.ToString() ?? "", t.NombreResponsable ?? "",
            t.FechaCreacion, t.FechaUltimaActividad, t.FechaObjetivo, ReglasTicket.Vencido(t.Estado, t.FechaObjetivo, ahora),
            t.FechaPrimeraRespuesta, t.FechaResolucion, t.FechaCierre, t.Calificacion, t.ComentarioCalificacion ?? "",
            creado is null ? [] : deEvento[creado.Id].ToList(),
            t.Eventos.Select(e => new EventoTicketDto(
                e.Id, e.Tipo, e.EstadoAnterior, e.EstadoNuevo, e.Texto ?? "", e.Interno, e.DeLaPlataforma, e.NombreUsuario, e.Fecha,
                e.Tipo == TipoEventoTicket.Creado ? [] : deEvento[e.Id].ToList())).ToList(),
            t.Reuniones.Select(r => new ReunionTicketDto(
                r.Id, r.Fecha, r.DuracionMinutos, r.Modalidad, r.Enlace ?? "", r.Motivo, r.Estado, r.Notas ?? "", r.NombreOrganizador)).ToList(),
            plataforma ? ReglasTicket.Siguientes(t.Estado, t.Tipo) : [],
            plataforma ? new AccionesClienteDto(false, false, false, false, false) : ReglasTicket.AccionesCliente(t.Estado));
    }

    /// <summary>Un adjunto del ticket (404 si el ticket no es visible para quien consulta o es de una nota interna).</summary>
    public async Task<(string Nombre, string Tipo, byte[] Contenido)> AdjuntoAsync(Guid id, Guid idAdjunto, bool plataforma, CancellationToken ct = default)
    {
        if (!await Consulta(plataforma).AnyAsync(t => t.Id == id, ct))
        {
            throw ErrorNegocio.NoEncontrado("El ticket");
        }
        var adjunto = await db.AdjuntosTicket.IgnoreQueryFilters([FiltrosConsulta.Empresa]).AsNoTracking()
            .Where(a => a.Id == idAdjunto && a.IdTicket == id && (plataforma || a.Evento == null || !a.Evento.Interno))
            .Select(a => new { a.Nombre, a.Tipo, a.Contenido })
            .FirstOrDefaultAsync(ct)
            ?? throw ErrorNegocio.NoEncontrado("El archivo");
        return (adjunto.Nombre, adjunto.Tipo, adjunto.Contenido);
    }

    // --- Apoyo -----------------------------------------------------------------------------------

    /// <summary>Aviso que se manda después de guardar (null = ninguno, p. ej. nota interna o asignación).</summary>
    private sealed record Aviso(bool DeLaPlataforma, string Titulo, string? Texto);

    private IQueryable<TicketSoporte> Consulta(bool plataforma) =>
        plataforma ? db.TicketsSoporte.IgnoreQueryFilters([FiltrosConsulta.Empresa]) : db.TicketsSoporte;

    private async Task<TicketDto> ModificarAsync(Guid id, bool plataforma, Func<TicketSoporte, string, Aviso?> cambio, CancellationToken ct)
    {
        if (plataforma) db.UsarModoSistema();
        var ticket = await Consulta(plataforma).Include(t => t.Reuniones).FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw ErrorNegocio.NoEncontrado("El ticket");
        var nombre = await NombreUsuarioAsync(ct);
        var aviso = cambio(ticket, nombre);
        if (plataforma && aviso is not null)
        {
            ticket.FechaPrimeraRespuesta ??= Ahora;
        }
        ticket.FechaUltimaActividad = Ahora;
        await db.SaveChangesAsync(ct);

        if (aviso is not null)
        {
            if (aviso.DeLaPlataforma)
            {
                await avisos.DeLaPlataformaAsync(ticket, aviso.Titulo, aviso.Texto ?? "", ct);
            }
            else
            {
                await avisos.DeLaEmpresaAsync(ticket, await NombreEmpresaAsync(ticket.IdEmpresa, ct), aviso.Titulo, aviso.Texto, ct);
            }
        }
        return await ObtenerAsync(id, plataforma, ct);
    }

    /// <summary>Cambia el estado, deja el evento y actualiza las fechas de resolución y cierre.</summary>
    private void Cambiar(TicketSoporte t, EstadoTicket hacia, string? texto, bool plataforma, string nombre)
    {
        Evento(t, TipoEventoTicket.CambioEstado, t.Estado, hacia, texto, false, plataforma, nombre);
        t.Estado = hacia;
        t.FechaResolucion = hacia switch
        {
            EstadoTicket.Resuelto => Ahora,
            EstadoTicket.Cerrado => t.FechaResolucion ?? Ahora,
            _ => ReglasTicket.Finalizados.Contains(hacia) ? t.FechaResolucion : null,
        };
        t.FechaCierre = ReglasTicket.Finalizados.Contains(hacia) ? Ahora : null;
    }

    private EventoTicket Evento(
        TicketSoporte t, TipoEventoTicket tipo, EstadoTicket? desde, EstadoTicket? hacia, string? texto, bool interno, bool plataforma, string nombre)
    {
        var evento = new EventoTicket
        {
            IdTicket = t.Id, Tipo = tipo, EstadoAnterior = desde, EstadoNuevo = hacia, Texto = Texto.Opcional(texto), Interno = interno,
            DeLaPlataforma = plataforma, IdUsuario = contexto.IdUsuario, NombreUsuario = nombre, Fecha = Ahora,
        };
        // Por el DbSet (también para un ticket nuevo: EF lo inserta primero por la llave).
        db.EventosTicket.Add(evento);
        return evento;
    }

    private sealed record ArchivoNuevo(string Nombre, string Tipo, byte[] Contenido);

    private static List<ArchivoNuevo> Decodificar(IReadOnlyList<AdjuntoNuevoDto>? adjuntos)
    {
        var lista = new List<ArchivoNuevo>();
        foreach (var a in adjuntos ?? [])
        {
            var url = a.Url!;
            var coma = url.IndexOf(',');
            byte[] contenido;
            try
            {
                contenido = Convert.FromBase64String(url[(coma + 1)..]);
            }
            catch (FormatException)
            {
                throw new ErrorNegocio($"El archivo {a.Nombre} no es válido.", CodigoError.Validacion);
            }
            if (contenido.Length > ReglasTicket.TamanoMaximoAdjunto)
            {
                throw new ErrorNegocio($"El archivo {a.Nombre} supera 1 MB.", CodigoError.Validacion);
            }
            lista.Add(new ArchivoNuevo(a.Nombre!.Trim(), a.Tipo!, contenido));
        }
        return lista;
    }

    private void Adjuntar(TicketSoporte t, EventoTicket evento, List<ArchivoNuevo> archivos, DateTime ahora)
    {
        foreach (var a in archivos)
        {
            var adjunto = new AdjuntoTicket
            {
                IdTicket = t.Id, IdEvento = evento.Id, Nombre = a.Nombre, Tipo = a.Tipo, Tamano = a.Contenido.Length, Contenido = a.Contenido, FechaCreacion = ahora,
            };
            db.AdjuntosTicket.Add(adjunto);
        }
    }

    private async Task<string> NombreUsuarioAsync(CancellationToken ct) =>
        contexto.IdUsuario is { } id
            ? await db.Usuarios.IgnoreQueryFilters().Where(u => u.Id == id).Select(u => u.Nombre).FirstOrDefaultAsync(ct) ?? ""
            : "";

    private async Task<string> NombreEmpresaAsync(Guid idEmpresa, CancellationToken ct) =>
        await db.Empresas.IgnoreQueryFilters([FiltrosConsulta.Empresa]).Where(e => e.Id == idEmpresa).Select(e => e.Nombre).FirstOrDefaultAsync(ct) ?? "";

    private static string Modalidad(ModalidadReunion m) => m switch
    {
        ModalidadReunion.Virtual => "Videollamada",
        ModalidadReunion.Presencial => "Presencial",
        _ => "Llamada",
    };

    /// <summary>Clave del resumen igual que el JSON (<c>EnQa</c> → <c>EN_QA</c>).</summary>
    private static string TextoEstado(EstadoTicket e) => System.Text.Json.JsonNamingPolicy.SnakeCaseUpper.ConvertName(e.ToString());
}
