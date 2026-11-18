using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Domain.Enums;
using RutaRola.Domain.Finanzas;

namespace RutaRola.Application.Finanzas;

/// <summary>
/// Estudiante cotizado: registrado (<c>estudianteId</c>) o solo nombre (cliente nuevo, <c>null</c>), con su colegio
/// (registrado o escrito) y su valor MENSUAL (cada estudiante puede tener un valor distinto).
/// </summary>
public record EstudianteCotizadoDto(Guid? EstudianteId, string Nombre, Guid? ColegioId = null, string NombreColegio = "", decimal Valor = 0);

/// <summary>
/// <c>acudienteId</c> es <c>null</c> cuando el acudiente no está registrado. <c>valorMensual</c> = suma de los
/// estudiantes; <c>meses</c> = del mes inicial al final; <c>valorTotal</c> = mensual × meses. <c>facturas</c>: las que se
/// generaron al aprobarla (vacía si aún no se facturó).
/// </summary>
public record CotizacionDto(
    Guid Id, string Numero, DateOnly Fecha, bool Registrados, Guid? AcudienteId, string NombreAcudiente, string TelefonoAcudiente,
    IReadOnlyList<EstudianteCotizadoDto> Estudiantes, string PeriodoInicio, string PeriodoFin, int Meses, decimal ValorMensual,
    decimal ValorTotal, EstadoCotizacion Estado, string Observaciones, DateTime FechaCreacion, DateTime? FechaActualizacion,
    IReadOnlyList<Opcion> Facturas, string CorreoAcudiente = "");

/// <summary><c>numero</c>, los valores totales y los nombres de registrados los calcula el servidor.</summary>
public record SolicitudCotizacion(
    DateOnly? Fecha, bool Registrados, Guid? AcudienteId, string? NombreAcudiente, string? TelefonoAcudiente,
    IReadOnlyList<EstudianteCotizadoDto>? Estudiantes, string? PeriodoInicio, string? PeriodoFin, EstadoCotizacion? Estado,
    string? Observaciones);

public record FiltrosCotizacion(string? Texto = null, string? Estado = null, DateOnly? Desde = null, DateOnly? Hasta = null);

/// <summary>Aprueba la cotización y crea una factura por estudiante (emitida en <c>fechaEmision</c>, hoy si no llega).</summary>
public record SolicitudFacturarCotizacion(DateOnly? FechaEmision, string? Concepto);

public record ResultadoFacturacion(CotizacionDto Cotizacion, IReadOnlyList<FacturaDto> Facturas);

public class ValidadorSolicitudCotizacion : AbstractValidator<SolicitudCotizacion>
{
    public ValidadorSolicitudCotizacion()
    {
        RuleFor(x => x.Fecha).NotNull().WithMessage("Ingresa la fecha de la cotización.");
        RuleFor(x => x.AcudienteId).NotEmpty().When(x => x.Registrados).WithMessage("Elige el acudiente registrado.");
        RuleFor(x => x.NombreAcudiente).NotEmpty().When(x => !x.Registrados).WithMessage("Ingresa el nombre del acudiente.")
            .MaximumLength(100).WithMessage("El nombre admite máximo 100 caracteres.");
        RuleFor(x => x.TelefonoAcudiente).TelefonoValido().When(x => !x.Registrados && !string.IsNullOrWhiteSpace(x.TelefonoAcudiente));
        RuleFor(x => x.Estudiantes).NotEmpty().WithMessage("Agrega al menos un estudiante a la cotización.")
            .Must(e => e is null || e.Count <= 20).WithMessage("Una cotización admite máximo 20 estudiantes.");
        RuleForEach(x => x.Estudiantes).ChildRules(e =>
        {
            e.RuleFor(x => x.Nombre).NotEmpty().WithMessage("Falta el nombre de un estudiante.")
                .MaximumLength(150).WithMessage("El nombre admite máximo 150 caracteres.");
            e.RuleFor(x => x.NombreColegio).MaximumLength(150).WithMessage("El colegio admite máximo 150 caracteres.");
            e.RuleFor(x => x.Valor).GreaterThanOrEqualTo(1).WithMessage("El valor mensual de cada estudiante debe ser mayor que cero.")
                .LessThan(1_000_000_000).WithMessage("El valor es demasiado alto.");
        });
        RuleFor(x => x.PeriodoInicio).PeriodoValido("mes inicial del servicio");
        RuleFor(x => x.PeriodoFin).PeriodoValido("mes final del servicio");
        RuleFor(x => x.PeriodoFin)
            .Must((s, fin) => string.CompareOrdinal(fin, s.PeriodoInicio) >= 0)
            .When(x => Periodos.EsValido(x.PeriodoInicio) && Periodos.EsValido(x.PeriodoFin))
            .WithMessage("El mes final no puede ser anterior al mes inicial.");
        RuleFor(x => x.Estado).NotNull().WithMessage("Selecciona el estado.");
        RuleFor(x => x.Observaciones).MaximumLength(500).WithMessage("Las observaciones admiten máximo 500 caracteres.");
    }
}

public class ValidadorSolicitudFacturarCotizacion : AbstractValidator<SolicitudFacturarCotizacion>
{
    public ValidadorSolicitudFacturarCotizacion()
    {
        RuleFor(x => x.Concepto).MaximumLength(120).WithMessage("El concepto admite máximo 120 caracteres.");
    }
}

/// <summary>
/// Cotizaciones a acudientes registrados o nuevos: número consecutivo del servidor, uno o varios estudiantes de uno o
/// varios colegios con su valor mensual, y los meses del servicio (como una factura). Para registrados, los nombres, el
/// teléfono y el colegio salen de sus registros (de la empresa; si no, 404). Aprobada, se convierte en facturas (una por
/// estudiante) y desde ese momento queda de solo lectura.
/// </summary>
public class ServicioCotizaciones(IBaseDatos db, ServicioFacturas facturas, TimeProvider reloj)
    : ServicioCrud<Cotizacion, CotizacionDto, SolicitudCotizacion, FiltrosCotizacion>(db)
{
    protected override string Entidad => "La cotización";
    protected override DbSet<Cotizacion> Conjunto => Db.Cotizaciones;

    protected override IQueryable<Cotizacion> Filtrar(IQueryable<Cotizacion> consulta, FiltrosCotizacion f)
    {
        if (Texto.Opcional(f.Texto) is { } texto)
        {
            var busqueda = Busqueda.Normalizar(texto);
            consulta = consulta.Where(x => FuncionesBd.SinTildes(x.Numero + " " + x.NombreAcudiente).Contains(busqueda)
                || x.Estudiantes.Any(e => FuncionesBd.SinTildes(e.Nombre + " " + e.NombreColegio).Contains(busqueda)));
        }
        if (TextoEnum.Leer<EstadoCotizacion>(f.Estado) is { } estado)
        {
            consulta = consulta.Where(x => x.Estado == estado);
        }
        if (f.Desde is { } desde)
        {
            consulta = consulta.Where(x => x.Fecha >= desde);
        }
        if (f.Hasta is { } hasta)
        {
            consulta = consulta.Where(x => x.Fecha <= hasta);
        }
        return consulta;
    }

    protected override IOrderedQueryable<Cotizacion> Ordenar(IQueryable<Cotizacion> consulta) =>
        consulta.OrderByDescending(x => x.Numero.Length).ThenByDescending(x => x.Numero).ThenBy(x => x.Id);

    protected override IQueryable<Cotizacion> Incluir(IQueryable<Cotizacion> consulta) => consulta.Include(x => x.Estudiantes);

    protected override string Etiqueta(Cotizacion c) => $"{c.Numero} · {c.NombreAcudiente}";

    protected override async Task<IReadOnlyList<CotizacionDto>> MapearAsync(IReadOnlyList<Cotizacion> registros, bool detalle, CancellationToken ct)
    {
        var ids = registros.Select(c => c.Id).ToList();
        var generadas = await Db.Facturas.AsNoTracking().Where(f => f.IdCotizacion != null && ids.Contains(f.IdCotizacion.Value))
            .OrderBy(f => f.NumeroFactura.Length).ThenBy(f => f.NumeroFactura)
            .Select(f => new { f.IdCotizacion, f.Id, f.NumeroFactura, f.NombreEstudiante }).ToListAsync(ct);
        var idsAcudientes = registros.Where(c => c.IdAcudiente != null).Select(c => c.IdAcudiente!.Value).Distinct().ToList();
        var correos = detalle && idsAcudientes.Count > 0
            ? await Db.Acudientes.AsNoTracking().Where(a => idsAcudientes.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Correo ?? "", ct)
            : new Dictionary<Guid, string>();
        return registros.Select(c => new CotizacionDto(
            c.Id, c.Numero, c.Fecha, c.IdAcudiente is not null, c.IdAcudiente, c.NombreAcudiente, c.TelefonoAcudiente ?? "",
            c.Estudiantes.OrderBy(e => e.Orden).Select(e => new EstudianteCotizadoDto(e.IdEstudiante, e.Nombre, e.IdColegio, e.NombreColegio, e.Valor)).ToList(),
            Periodos.Texto(c.PeriodoInicio), Periodos.Texto(c.PeriodoFin), Meses(c), c.ValorMensual, c.ValorTotal, c.Estado,
            c.Observaciones ?? "", c.FechaCreacion, c.FechaActualizacion,
            generadas.Where(f => f.IdCotizacion == c.Id).Select(f => new Opcion(f.Id, $"{f.NumeroFactura} · {f.NombreEstudiante}")).ToList(),
            c.IdAcudiente is { } a ? correos.GetValueOrDefault(a, "") : "")).ToList();
    }

    private static int Meses(Cotizacion c) => Periodos.Entre(c.PeriodoInicio, c.PeriodoFin).Count();

    protected override async Task AplicarAsync(Cotizacion c, SolicitudCotizacion s, bool nuevo, CancellationToken ct)
    {
        if (c.FechaFacturacion is not null)
        {
            throw new ErrorNegocio($"La cotización {c.Numero} ya se convirtió en facturas y no se puede modificar.", CodigoError.Conflicto);
        }
        var lista = s.Estudiantes!;
        Domain.Personas.Acudiente? acudiente = null;
        var registrados = new Dictionary<Guid, Domain.Personas.Estudiante>();
        if (s.Registrados)
        {
            acudiente = await Db.Acudientes.FirstOrDefaultAsync(a => a.Id == s.AcudienteId, ct) ?? throw ErrorNegocio.NoEncontrado("El acudiente");
            var ids = lista.Select(e => e.EstudianteId).OfType<Guid>().Distinct().ToList();
            var estudiantes = await Db.Estudiantes.Where(e => ids.Contains(e.Id)).ToListAsync(ct);
            if (estudiantes.Count != ids.Count)
            {
                throw new ErrorNegocio("Uno o más estudiantes no existen o fueron eliminados.", CodigoError.NoEncontrado);
            }
            registrados = estudiantes.ToDictionary(e => e.Id);
        }
        // Colegios: los registrados se buscan en la empresa (otro id = 404) y se toma su nombre.
        var idsColegios = lista.Select(e => e.EstudianteId is { } id && registrados.TryGetValue(id, out var est) ? est.IdColegio : e.ColegioId)
            .OfType<Guid>().Distinct().ToList();
        var colegios = await Db.Colegios.Where(x => idsColegios.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.RazonSocial, ct);
        if (colegios.Count != idsColegios.Count)
        {
            throw ErrorNegocio.NoEncontrado("El colegio");
        }

        if (nuevo)
        {
            c.Numero = await Consecutivos.SiguienteAsync(Db.Cotizaciones.Select(x => x.Numero), "COT", ct);
        }
        c.Fecha = s.Fecha!.Value;
        c.IdAcudiente = acudiente?.Id;
        c.NombreAcudiente = acudiente?.NombreCompleto ?? s.NombreAcudiente!.Trim();
        c.TelefonoAcudiente = acudiente?.Telefono ?? Texto.Opcional(s.TelefonoAcudiente);
        c.PeriodoInicio = Periodos.Leer(s.PeriodoInicio!);
        c.PeriodoFin = Periodos.Leer(s.PeriodoFin!);
        c.Estado = s.Estado!.Value;
        c.Observaciones = Texto.Opcional(s.Observaciones);
        c.Estudiantes.Clear();
        c.Estudiantes.AddRange(lista.Select((e, i) =>
        {
            var registrado = s.Registrados && e.EstudianteId is { } id ? registrados[id] : null;
            var idColegio = registrado?.IdColegio ?? e.ColegioId;
            return new CotizacionEstudiante
            {
                IdEstudiante = registrado?.Id,
                Nombre = registrado?.NombreCompleto ?? e.Nombre.Trim(),
                IdColegio = idColegio,
                NombreColegio = idColegio is { } col ? colegios[col] : e.NombreColegio.Trim(),
                Valor = Math.Round(e.Valor, 2),
                Orden = i + 1,
            };
        }));
        c.ValorMensual = c.Estudiantes.Sum(e => e.Valor);
        c.ValorTotal = c.ValorMensual * Meses(c);
    }

    public override Task<string?> MotivoNoEliminarAsync(Guid id, CancellationToken ct = default) =>
        Db.Facturas.AnyAsync(f => f.IdCotizacion == id, ct)
            .ContinueWith(t => t.Result ? "No se puede eliminar: ya se convirtió en facturas. Consulta las facturas generadas." : null, ct);

    /// <summary>
    /// Aprueba la cotización y crea una factura vigente por estudiante (valor mensual del estudiante, meses de la
    /// cotización). Exige acudiente y estudiantes registrados y aplica las reglas de las facturas (el estudiante a cargo del
    /// acudiente, sin facturas vigentes que se crucen). Todo o nada: si una falla, no se crea ninguna.
    /// </summary>
    public async Task<ResultadoFacturacion> FacturarAsync(Guid id, SolicitudFacturarCotizacion s, CancellationToken ct = default)
    {
        var c = await BuscarAsync(id, ct);
        if (c.FechaFacturacion is not null)
        {
            throw new ErrorNegocio($"La cotización {c.Numero} ya se convirtió en facturas.", CodigoError.Conflicto);
        }
        if (c.Estado == EstadoCotizacion.Rechazada)
        {
            throw new ErrorNegocio("La cotización está rechazada. Cámbiala a Pendiente o Aprobada para facturarla.", CodigoError.Validacion);
        }
        if (c.IdAcudiente is not { } idAcudiente)
        {
            throw new ErrorNegocio(
                "Para facturar, registra al acudiente y a sus estudiantes y edita la cotización eligiéndolos (cliente registrado).",
                CodigoError.Validacion);
        }
        if (c.Estudiantes.Where(e => e.IdEstudiante is null).Select(e => e.Nombre).ToList() is { Count: > 0 } sinRegistro)
        {
            throw new ErrorNegocio(
                $"Registra a {string.Join(", ", sinRegistro)} y edita la cotización eligiéndolos para poder facturar.", CodigoError.Validacion);
        }

        var fecha = s.FechaEmision ?? Fechas.Hoy(reloj);
        var concepto = Texto.Opcional(s.Concepto) ?? "Servicio de transporte escolar";
        var creadas = new List<FacturaDto>();
        await using var transaccion = await Db.Database.BeginTransactionAsync(ct);
        foreach (var e in c.Estudiantes.OrderBy(e => e.Orden))
        {
            var factura = await facturas.CrearAsync(new SolicitudFactura(
                fecha, idAcudiente, e.IdEstudiante, e.Valor, Periodos.Texto(c.PeriodoInicio), Periodos.Texto(c.PeriodoFin), concepto,
                EstadoRegistro.Activo, $"Generada desde la cotización {c.Numero}."), ct);
            var entidad = await Db.Facturas.FirstAsync(f => f.Id == factura.Id, ct);
            entidad.IdCotizacion = c.Id;
            creadas.Add(factura);
        }
        c.Estado = EstadoCotizacion.Aprobada;
        c.FechaFacturacion = reloj.GetUtcNow().UtcDateTime;
        await Db.SaveChangesAsync(ct);
        await transaccion.CommitAsync(ct);
        return new ResultadoFacturacion(await ObtenerAsync(id, ct), creadas.Select(f => f with { CotizacionId = c.Id.ToString(), NumeroCotizacion = c.Numero }).ToList());
    }
}
