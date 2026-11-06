using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Domain.Comunicacion;
using RutaRola.Domain.Enums;
using RutaRola.Domain.Finanzas;

namespace RutaRola.Application.Finanzas;

/// <summary>Mensaje de WhatsApp (solo <c>cuerpo</c>) o correo (<c>asunto</c>, <c>cuerpo</c>, hasta 3 adjuntos).</summary>
public record PlantillaDto(Guid Id, string Asunto, string Cuerpo, IReadOnlyList<ArchivoAdjuntoDto> Adjuntos, EstadoPlantilla Estado);

public record ProgramacionDto(
    Guid Id, string Nombre, Guid FacturaId, string NumeroFactura, int DiaLimitePago, int DiasPrevios, bool Whatsapp,
    IReadOnlyList<PlantillaDto> MensajesWhatsapp, bool Email, IReadOnlyList<PlantillaDto> Correos, string Estado,
    DateTime FechaCreacion, DateTime? FechaActualizacion);

/// <summary><c>id</c>: el de una plantilla existente de esta programación (se conserva) o uno nuevo del navegador.</summary>
public record SolicitudPlantilla(Guid? Id, string? Asunto, string? Cuerpo, IReadOnlyList<ArchivoAdjuntoDto>? Adjuntos, EstadoPlantilla? Estado);

public record SolicitudProgramacion(
    string? Nombre, Guid? FacturaId, int? DiaLimitePago, int? DiasPrevios, bool Whatsapp, IReadOnlyList<SolicitudPlantilla>? MensajesWhatsapp,
    bool Email, IReadOnlyList<SolicitudPlantilla>? Correos, string? Estado);

public record FiltrosProgramacion(string? Texto = null, Guid? FacturaId = null, string? Estado = null);

/// <summary>Envío realizado por el proceso automático del servidor (fase 7).</summary>
public record EnvioDto(Guid Id, Guid ProgramacionId, DateTime Fecha, CanalEnvio Canal, string Destinatario, string Periodo, EstadoEnvio Estado, string Detalle);

public class ValidadorSolicitudProgramacion : AbstractValidator<SolicitudProgramacion>
{
    public const int MaximoAdjuntos = 3;

    public ValidadorSolicitudProgramacion()
    {
        RuleFor(x => x.Nombre).NotEmpty().WithMessage("Ingresa el nombre de la programación.")
            .MaximumLength(80).WithMessage("El nombre admite máximo 80 caracteres.");
        RuleFor(x => x.FacturaId).NotEmpty().WithMessage("Selecciona la factura.");
        RuleFor(x => x.DiaLimitePago).NotNull().WithMessage("Selecciona el día límite de pago.")
            .InclusiveBetween(1, 31).WithMessage("El día límite debe estar entre 1 y 31.");
        RuleFor(x => x.DiasPrevios).NotNull().WithMessage("Selecciona cuántos días antes se envía.")
            .InclusiveBetween(1, 10).WithMessage("Los días previos deben estar entre 1 y 10.");
        RuleFor(x => x.Estado).EstadoRegistroValido();
        RuleFor(x => x.MensajesWhatsapp).Must(m => m is null || m.Count <= 10).WithMessage("Máximo 10 mensajes de WhatsApp.");
        RuleFor(x => x.Correos).Must(m => m is null || m.Count <= 10).WithMessage("Máximo 10 correos.");
        RuleForEach(x => x.MensajesWhatsapp).ChildRules(Plantilla(false));
        RuleForEach(x => x.Correos).ChildRules(Plantilla(true));
    }

    private static Action<InlineValidator<SolicitudPlantilla>> Plantilla(bool correo) => p =>
    {
        p.RuleFor(x => x.Cuerpo).NotEmpty().WithMessage("Escribe el mensaje.")
            .MaximumLength(1000).WithMessage("El mensaje admite máximo 1000 caracteres.");
        p.RuleFor(x => x.Estado).NotNull().WithMessage("Indica el estado del mensaje.");
        if (correo)
        {
            p.RuleFor(x => x.Asunto).NotEmpty().WithMessage("Escribe el asunto del correo.")
                .MaximumLength(150).WithMessage("El asunto admite máximo 150 caracteres.");
            p.RuleFor(x => x.Adjuntos).Must(a => a is null || a.Count <= MaximoAdjuntos)
                .WithMessage($"Un correo admite máximo {MaximoAdjuntos} adjuntos.");
            p.RuleForEach(x => x.Adjuntos).ReglasAdjunto("adjunto");
        }
    };
}

/// <summary>
/// Recordatorios automáticos de cobro de una factura: una programación por factura (409); cada canal
/// habilitado con exactamente un mensaje "En uso" (422); activa con al menos un canal. El envío lo
/// hace el proceso del servidor <c>diasPrevios</c> días antes de <c>diaLimitePago</c> (fase 7).
/// </summary>
public class ServicioProgramaciones(IBaseDatos db, IContextoUsuario contexto)
    : ServicioCrud<ProgramacionCobro, ProgramacionDto, SolicitudProgramacion, FiltrosProgramacion>(db)
{
    protected override string Entidad => "La programación";
    protected override DbSet<ProgramacionCobro> Conjunto => Db.ProgramacionesCobro;

    protected override IQueryable<ProgramacionCobro> Filtrar(IQueryable<ProgramacionCobro> consulta, FiltrosProgramacion f)
    {
        if (Texto.Opcional(f.Texto) is { } texto)
        {
            var busqueda = Busqueda.Normalizar(texto);
            consulta = consulta.Where(x => FuncionesBd.SinTildes(x.Nombre + " " + x.Factura.NumeroFactura).Contains(busqueda));
        }
        if (f.FacturaId is { } factura)
        {
            consulta = consulta.Where(x => x.IdFactura == factura);
        }
        if (Texto.Opcional(f.Estado) is { } estado)
        {
            var activo = EstadoRegistro.EsActivo(estado);
            consulta = consulta.Where(x => x.Activo == activo);
        }
        return consulta;
    }

    protected override IOrderedQueryable<ProgramacionCobro> Ordenar(IQueryable<ProgramacionCobro> consulta) =>
        consulta.OrderBy(x => FuncionesBd.SinTildes(x.Nombre)).ThenBy(x => x.Id);

    protected override IQueryable<ProgramacionCobro> Incluir(IQueryable<ProgramacionCobro> consulta) =>
        consulta.Include(x => x.Factura).Include(x => x.Plantillas).ThenInclude(p => p.Adjuntos).ThenInclude(a => a.Archivo);

    protected override string Etiqueta(ProgramacionCobro p) => p.Nombre;

    protected override Task<IReadOnlyList<ProgramacionDto>> MapearAsync(IReadOnlyList<ProgramacionCobro> registros, bool detalle, CancellationToken ct)
    {
        IReadOnlyList<PlantillaDto> Plantillas(ProgramacionCobro p, CanalEnvio canal) => p.Plantillas
            .Where(x => x.Canal == canal).OrderBy(x => x.Orden)
            .Select(x => new PlantillaDto(x.Id, x.Asunto ?? "", x.Cuerpo, x.Adjuntos.Select(a => Adjuntos.Dto(a.Archivo, detalle)).ToList(), x.Estado))
            .ToList();
        return Task.FromResult<IReadOnlyList<ProgramacionDto>>(registros.Select(p => new ProgramacionDto(
            p.Id, p.Nombre, p.IdFactura, p.Factura.NumeroFactura, p.DiaLimitePago, p.DiasPrevios, p.Whatsapp, Plantillas(p, CanalEnvio.Whatsapp),
            p.Email, Plantillas(p, CanalEnvio.Email), EstadoRegistro.Texto(p.Activo), p.FechaCreacion, p.FechaActualizacion)).ToList());
    }

    protected override async Task AplicarAsync(ProgramacionCobro p, SolicitudProgramacion s, bool nuevo, CancellationToken ct)
    {
        var factura = await Db.Facturas.FirstOrDefaultAsync(f => f.Id == s.FacturaId, ct) ?? throw ErrorNegocio.NoEncontrado("La factura");
        var idActual = nuevo ? (Guid?)null : p.Id;
        var otra = await Db.ProgramacionesCobro.AsNoTracking().Where(x => x.Id != idActual && x.IdFactura == factura.Id)
            .Select(x => x.Nombre).FirstOrDefaultAsync(ct);
        if (otra is not null)
        {
            throw new ErrorNegocio($"La factura {factura.NumeroFactura} ya tiene la programación \"{otra}\".", CodigoError.Duplicado);
        }

        var whatsapp = s.MensajesWhatsapp ?? [];
        var correos = s.Correos ?? [];
        static int EnUso(IReadOnlyList<SolicitudPlantilla> lista) => lista.Count(x => x.Estado == EstadoPlantilla.EnUso);
        if (s.Whatsapp && EnUso(whatsapp) != 1)
        {
            throw new ErrorNegocio("Marca exactamente un mensaje de WhatsApp \"En uso\".", CodigoError.Validacion);
        }
        if (s.Email && EnUso(correos) != 1)
        {
            throw new ErrorNegocio("Marca exactamente un correo \"En uso\".", CodigoError.Validacion);
        }
        var activa = EstadoRegistro.EsActivo(s.Estado);
        if (activa && !s.Whatsapp && !s.Email)
        {
            throw new ErrorNegocio("Una programación activa debe enviar por WhatsApp, correo o ambos.", CodigoError.Validacion);
        }
        // En un canal apagado puede quedar a lo sumo un "En uso" (índice único por canal).
        if (EnUso(whatsapp) > 1 || EnUso(correos) > 1)
        {
            throw new ErrorNegocio("Solo un mensaje por canal puede estar \"En uso\".", CodigoError.Validacion);
        }

        p.Nombre = s.Nombre!.Trim();
        p.IdFactura = factura.Id;
        p.DiaLimitePago = s.DiaLimitePago!.Value;
        p.DiasPrevios = s.DiasPrevios!.Value;
        p.Whatsapp = s.Whatsapp;
        p.Email = s.Email;
        p.Activo = activa;
        await SincronizarPlantillasAsync(p, CanalEnvio.Whatsapp, whatsapp, ct);
        await SincronizarPlantillasAsync(p, CanalEnvio.Email, correos, ct);
    }

    /// <summary>
    /// Conserva las plantillas que siguen (por id), crea las nuevas con id del servidor y borra las quitadas
    /// con sus archivos. Los adjuntos: data URL → archivo nuevo; si no, se conserva el de igual nombre y tamaño.
    /// </summary>
    private async Task SincronizarPlantillasAsync(ProgramacionCobro p, CanalEnvio canal, IReadOnlyList<SolicitudPlantilla> lista, CancellationToken ct)
    {
        var actuales = p.Plantillas.Where(x => x.Canal == canal).ToList();
        foreach (var quitada in actuales.Where(x => lista.All(s => s.Id != x.Id)))
        {
            await QuitarAdjuntosAsync(quitada, quitada.Adjuntos.ToList(), ct);
            p.Plantillas.Remove(quitada);
        }

        for (var i = 0; i < lista.Count; i++)
        {
            var s = lista[i];
            var plantilla = actuales.FirstOrDefault(x => x.Id == s.Id);
            if (plantilla is null)
            {
                plantilla = new PlantillaMensaje { Canal = canal };
                p.Plantillas.Add(plantilla);
            }
            plantilla.Asunto = canal == CanalEnvio.Email ? Texto.Opcional(s.Asunto) : null;
            plantilla.Cuerpo = s.Cuerpo!.Trim();
            plantilla.Estado = s.Estado!.Value;
            plantilla.Orden = i + 1;

            var pedidos = canal == CanalEnvio.Email ? s.Adjuntos ?? [] : [];
            var conservados = new List<PlantillaMensajeArchivo>();
            foreach (var pedido in pedidos.Where(a => !Adjuntos.EsDataUrl(a.Url)))
            {
                var existente = plantilla.Adjuntos.FirstOrDefault(a => !conservados.Contains(a) && a.Archivo.Nombre == pedido.Nombre && a.Archivo.Tamano == pedido.Tamano);
                if (existente is not null)
                {
                    conservados.Add(existente);
                }
            }
            await QuitarAdjuntosAsync(plantilla, plantilla.Adjuntos.Except(conservados).ToList(), ct);
            foreach (var nuevo in pedidos.Where(a => Adjuntos.EsDataUrl(a.Url)))
            {
                var idArchivo = await Adjuntos.AplicarAsync(Db, null, nuevo, contexto.IdUsuario, "adjunto", ct);
                plantilla.Adjuntos.Add(new PlantillaMensajeArchivo { IdArchivo = idArchivo!.Value });
            }
        }
    }

    protected override async Task AntesDeEliminarAsync(ProgramacionCobro p, CancellationToken ct)
    {
        foreach (var plantilla in p.Plantillas)
        {
            await QuitarAdjuntosAsync(plantilla, plantilla.Adjuntos.ToList(), ct);
        }
    }

    /// <summary>Primero el enlace (se borra con la plantilla) y luego el archivo, que no tiene borrado en cascada.</summary>
    private async Task QuitarAdjuntosAsync(PlantillaMensaje plantilla, IReadOnlyList<PlantillaMensajeArchivo> adjuntos, CancellationToken ct)
    {
        foreach (var adjunto in adjuntos)
        {
            plantilla.Adjuntos.Remove(adjunto);
            await Adjuntos.EliminarAsync(Db, adjunto.IdArchivo, ct);
        }
    }

    /// <summary>Se puede eliminar; su historial de envíos se borra con ella.</summary>
    public override Task<string?> MotivoNoEliminarAsync(Guid id, CancellationToken ct = default) => Task.FromResult<string?>(null);

    /// <summary>Historial de envíos (más reciente primero); 404 si la programación no es de la empresa.</summary>
    public async Task<IReadOnlyList<EnvioDto>> EnviosAsync(Guid id, CancellationToken ct = default)
    {
        if (!await Db.ProgramacionesCobro.AnyAsync(p => p.Id == id, ct))
        {
            throw ErrorNegocio.NoEncontrado(Entidad);
        }
        var envios = await Db.EnviosProgramacion.AsNoTracking().Where(e => e.IdProgramacion == id)
            .OrderByDescending(e => e.Fecha).ThenBy(e => e.Canal).ToListAsync(ct);
        return envios.Select(e => new EnvioDto(e.Id, e.IdProgramacion, e.Fecha, e.Canal, e.Destinatario, Periodos.Texto(e.Periodo), e.Estado, e.Detalle ?? ""))
            .ToList();
    }

    /// <summary>Envíos por programación (columna del listado); los ids ajenos o inexistentes dan 0.</summary>
    public async Task<IReadOnlyDictionary<Guid, int>> ConteoEnviosAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default)
    {
        var conteo = await Db.EnviosProgramacion.AsNoTracking().Where(e => ids.Contains(e.IdProgramacion))
            .GroupBy(e => e.IdProgramacion).Select(g => new { g.Key, Total = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Total, ct);
        return ids.Distinct().ToDictionary(id => id, id => conteo.GetValueOrDefault(id));
    }
}
