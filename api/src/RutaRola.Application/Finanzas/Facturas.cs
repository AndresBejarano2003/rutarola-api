using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Domain.Enums;
using RutaRola.Domain.Finanzas;

namespace RutaRola.Application.Finanzas;

/// <summary>
/// Contrato mensual (<c>Factura</c> del frontend): <c>valorContratacion</c> es el valor MENSUAL y los
/// periodos van como <c>YYYY-MM</c>. Los nombres son los del momento de emitir. <c>periodoRetiro</c>: primer mes que ya
/// no se cobra (<c>""</c> = sin retiro); <c>valorTotal</c> = valor mensual × meses que se cobran. <c>cotizacionId</c>/
/// <c>numeroCotizacion</c>: la cotización aprobada de la que salió (<c>""</c> si se creó a mano).
/// </summary>
public record FacturaDto(
    Guid Id, string Numero, DateOnly FechaEmision, Guid AcudienteId, Guid EstudianteId, string NombreAcudiente,
    string NombreEstudiante, decimal ValorContratacion, string PeriodoInicio, string PeriodoFin, string Concepto, string Estado,
    string Observaciones, DateTime FechaCreacion, DateTime? FechaActualizacion,
    string PeriodoRetiro = "", string MotivoRetiro = "", decimal ValorTotal = 0, string CotizacionId = "", string NumeroCotizacion = "");

/// <summary>
/// Cuota (mes) de una factura con lo pagado y el saldo. <c>estado</c>: PAGADA | VENCIDA (pasó la fecha límite con saldo) |
/// PENDIENTE (mes en curso dentro del plazo) | PROGRAMADA (futura), igual que <c>calcularEstadoCuenta</c>.
/// </summary>
public record CuotaFacturaDto(string Periodo, decimal Valor, decimal Pagado, decimal Saldo, DateOnly FechaLimite, string Estado);

/// <summary><c>periodoRetiro</c> <c>YYYY-MM</c> = desde ese mes ya no se cobra; <c>""</c> = quitar el retiro.</summary>
public record SolicitudRetiro(string? PeriodoRetiro, string? Motivo);

public class ValidadorSolicitudRetiro : AbstractValidator<SolicitudRetiro>
{
    public ValidadorSolicitudRetiro()
    {
        RuleFor(x => x.PeriodoRetiro).Must(p => string.IsNullOrEmpty(p) || Periodos.EsValido(p))
            .WithMessage("Indica el mes del retiro como AAAA-MM.");
        RuleFor(x => x.Motivo).NotEmpty().When(x => !string.IsNullOrEmpty(x.PeriodoRetiro))
            .WithMessage("Cuenta el motivo del retiro.")
            .MaximumLength(300).WithMessage("El motivo admite máximo 300 caracteres.");
    }
}

/// <summary><c>numero</c> y los nombres los pone el servidor (si llegan, se ignoran).</summary>
public record SolicitudFactura(
    DateOnly? FechaEmision, Guid? AcudienteId, Guid? EstudianteId, decimal? ValorContratacion, string? PeriodoInicio,
    string? PeriodoFin, string? Concepto, string? Estado, string? Observaciones);

public record FiltrosFactura(string? Texto = null, Guid? AcudienteId = null, Guid? EstudianteId = null, string? Estado = null);

public class ValidadorSolicitudFactura : AbstractValidator<SolicitudFactura>
{
    public ValidadorSolicitudFactura()
    {
        RuleFor(x => x.FechaEmision).NotNull().WithMessage("Ingresa la fecha de emisión.");
        RuleFor(x => x.AcudienteId).NotEmpty().WithMessage("Selecciona el acudiente.");
        RuleFor(x => x.EstudianteId).NotEmpty().WithMessage("Selecciona el estudiante.");
        RuleFor(x => x.ValorContratacion).NotNull().WithMessage("Ingresa el valor mensual.")
            .GreaterThanOrEqualTo(1).WithMessage("El valor mensual debe ser mayor que cero.")
            .LessThan(1_000_000_000).WithMessage("El valor mensual es demasiado alto.");
        RuleFor(x => x.PeriodoInicio).PeriodoValido("mes inicial");
        RuleFor(x => x.PeriodoFin).PeriodoValido("mes final");
        RuleFor(x => x.PeriodoFin)
            .Must((s, fin) => string.CompareOrdinal(fin, s.PeriodoInicio) >= 0)
            .When(x => Periodos.EsValido(x.PeriodoInicio) && Periodos.EsValido(x.PeriodoFin))
            .WithMessage("El mes final no puede ser anterior al mes inicial.");
        RuleFor(x => x.Concepto).NotEmpty().WithMessage("Ingresa el concepto.")
            .MaximumLength(120).WithMessage("El concepto admite máximo 120 caracteres.");
        RuleFor(x => x.Estado).EstadoRegistroValido();
        RuleFor(x => x.Observaciones).MaximumLength(500).WithMessage("Las observaciones admiten máximo 500 caracteres.");
    }
}

/// <summary>
/// Facturas: número consecutivo del servidor; el estudiante debe estar a cargo del acudiente; una sola
/// factura vigente por estudiante en meses que se crucen; una línea de detalle por mes; no se elimina
/// con recibos o programación (se anula).
/// </summary>
public class ServicioFacturas(IBaseDatos db, Configuracion.ParametrosEmpresa parametros, TimeProvider reloj)
    : ServicioCrud<Factura, FacturaDto, SolicitudFactura, FiltrosFactura>(db)
{
    protected override string Entidad => "La factura";
    protected override DbSet<Factura> Conjunto => Db.Facturas;

    public static string Etiquetar(Factura f) => $"{f.NumeroFactura} · {f.NombreEstudiante}";

    protected override IQueryable<Factura> Filtrar(IQueryable<Factura> consulta, FiltrosFactura f)
    {
        if (Texto.Opcional(f.Texto) is { } texto)
        {
            var busqueda = Busqueda.Normalizar(texto);
            consulta = consulta.Where(x => FuncionesBd.SinTildes(x.NumeroFactura + " " + x.NombreAcudiente + " " + x.NombreEstudiante).Contains(busqueda));
        }
        if (f.AcudienteId is { } acudiente)
        {
            consulta = consulta.Where(x => x.IdAcudiente == acudiente);
        }
        if (f.EstudianteId is { } estudiante)
        {
            consulta = consulta.Where(x => x.IdEstudiante == estudiante);
        }
        if (Texto.Opcional(f.Estado) is { } estado)
        {
            var valor = Estado(estado);
            consulta = consulta.Where(x => x.Estado == valor);
        }
        return consulta;
    }

    /// <summary>Más reciente primero (FAC-0010 antes que FAC-0009).</summary>
    protected override IOrderedQueryable<Factura> Ordenar(IQueryable<Factura> consulta) =>
        consulta.OrderByDescending(x => x.NumeroFactura.Length).ThenByDescending(x => x.NumeroFactura).ThenBy(x => x.Id);

    protected override string Etiqueta(Factura f) => Etiquetar(f);

    public static EstadoFactura Estado(string? estado) => EstadoRegistro.EsActivo(estado) ? EstadoFactura.Vigente : EstadoFactura.Anulada;

    public static FacturaDto Mapear(Factura f) => Mapear(f, "");

    public static FacturaDto Mapear(Factura f, string numeroCotizacion) => new(
        f.Id, f.NumeroFactura, f.FechaEmision, f.IdAcudiente, f.IdEstudiante, f.NombreAcudiente, f.NombreEstudiante, f.ValorMensual,
        Periodos.Texto(f.PeriodoInicio), Periodos.Texto(f.PeriodoFin), f.Concepto, EstadoRegistro.Texto(f.Estado == EstadoFactura.Vigente),
        f.Observaciones ?? "", f.FechaCreacion, f.FechaActualizacion, Periodos.Texto(f.PeriodoRetiro), f.MotivoRetiro ?? "",
        f.ValorMensual * ReglasCuotas.MesesCobrables(f).Count, f.IdCotizacion?.ToString() ?? "", numeroCotizacion);

    protected override async Task<IReadOnlyList<FacturaDto>> MapearAsync(IReadOnlyList<Factura> registros, bool detalle, CancellationToken ct)
    {
        var ids = registros.Where(f => f.IdCotizacion != null).Select(f => f.IdCotizacion!.Value).Distinct().ToList();
        var numeros = ids.Count == 0
            ? new Dictionary<Guid, string>()
            : await Db.Cotizaciones.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Numero, ct);
        return registros.Select(f => Mapear(f, f.IdCotizacion is { } c ? numeros.GetValueOrDefault(c, "") : "")).ToList();
    }

    protected override IQueryable<Factura> Incluir(IQueryable<Factura> consulta) => consulta.Include(x => x.Detalle);

    protected override async Task AplicarAsync(Factura f, SolicitudFactura s, bool nuevo, CancellationToken ct)
    {
        // Referencias con el filtro de empresa: un id de otra empresa no existe (404).
        var acudiente = await Db.Acudientes.FirstOrDefaultAsync(a => a.Id == s.AcudienteId, ct) ?? throw ErrorNegocio.NoEncontrado("El acudiente");
        var estudiante = await Db.Estudiantes.FirstOrDefaultAsync(e => e.Id == s.EstudianteId, ct) ?? throw ErrorNegocio.NoEncontrado("El estudiante");
        if (!await Db.EstudiantesAcudientes.AnyAsync(x => x.IdEstudiante == estudiante.Id && x.IdAcudiente == acudiente.Id, ct))
        {
            throw new ErrorNegocio("El estudiante no está a cargo del acudiente elegido.", CodigoError.Validacion);
        }

        var inicio = Periodos.Leer(s.PeriodoInicio!);
        var fin = Periodos.Leer(s.PeriodoFin!);
        var estado = Estado(s.Estado);
        if (estado == EstadoFactura.Vigente)
        {
            var idActual = nuevo ? (Guid?)null : f.Id;
            var cruce = await Db.Facturas.AsNoTracking()
                .Where(x => x.Id != idActual && x.Estado == EstadoFactura.Vigente && x.IdEstudiante == estudiante.Id
                    && x.PeriodoInicio <= fin && inicio <= x.PeriodoFin)
                .Select(x => x.NumeroFactura).FirstOrDefaultAsync(ct);
            if (cruce is not null)
            {
                throw new ErrorNegocio(
                    $"{estudiante.NombreCompleto} ya tiene la factura vigente {cruce} en ese periodo. Anúlala o ajusta los meses.", CodigoError.Duplicado);
            }
        }

        if (nuevo)
        {
            f.NumeroFactura = await Consecutivos.SiguienteAsync(Db.Facturas.Select(x => x.NumeroFactura), "FAC", ct);
        }
        // Snapshot: se toma al emitir y solo cambia si cambia la persona.
        if (nuevo || f.IdAcudiente != acudiente.Id)
        {
            f.NombreAcudiente = acudiente.NombreCompleto;
        }
        if (nuevo || f.IdEstudiante != estudiante.Id)
        {
            f.NombreEstudiante = estudiante.NombreCompleto;
        }
        f.IdAcudiente = acudiente.Id;
        f.IdEstudiante = estudiante.Id;
        f.FechaEmision = s.FechaEmision!.Value;
        f.ValorMensual = Math.Round(s.ValorContratacion!.Value, 2);
        f.PeriodoInicio = inicio;
        f.PeriodoFin = fin;
        f.Concepto = s.Concepto!.Trim();
        f.Estado = estado;
        f.Observaciones = Texto.Opcional(s.Observaciones);

        // Un retiro que quedó fuera de los meses nuevos deja de aplicar.
        if (f.PeriodoRetiro is { } retiro && (retiro <= inicio || retiro > fin))
        {
            f.PeriodoRetiro = null;
            f.MotivoRetiro = null;
        }
        GenerarDetalle(f);
    }

    /// <summary>Una línea por mes que se cobra (sin los meses desde el retiro).</summary>
    public static void GenerarDetalle(Factura f)
    {
        var meses = ReglasCuotas.MesesCobrables(f);
        f.ValorTotal = f.ValorMensual * meses.Count;
        f.Detalle.Clear();
        f.Detalle.AddRange(meses.Select(p => new FacturaDetalle
        {
            Periodo = p,
            Descripcion = $"{f.Concepto} · {Periodos.Nombre(p, true)}",
            PrecioUnitario = f.ValorMensual,
            Total = f.ValorMensual,
        }));
    }

    public async Task<IReadOnlyList<CuotaFacturaDto>> CuotasAsync(Guid id, Guid? sinRecibo, CancellationToken ct = default)
    {
        var f = await Db.Facturas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw ErrorNegocio.NoEncontrado(Entidad);
        var pagos = await ReglasCuotas.PagosAsync(Db, [f.Id], ct, sinRecibo);
        var dia = await Db.ProgramacionesCobro.AsNoTracking().Where(p => p.IdFactura == id).Select(p => (int?)p.DiaLimitePago).FirstOrDefaultAsync(ct);
        var predeterminado = await parametros.DiaLimitePagoAsync(ct);
        var hoy = Fechas.Hoy(reloj);
        return ReglasCuotas.Cuotas(f, pagos.GetValueOrDefault(f.Id) ?? []).Select(c =>
        {
            var limite = Cobro.FechaLimite(c.Periodo, dia, predeterminado);
            var estado = c.Pagada ? "PAGADA" : hoy > limite ? "VENCIDA" : c.Periodo <= Periodos.De(hoy) ? "PENDIENTE" : "PROGRAMADA";
            return new CuotaFacturaDto(Periodos.Texto(c.Periodo), c.Valor, c.Pagado, c.Saldo, limite, estado);
        }).ToList();
    }

    /// <summary>
    /// Registra (o quita, con <c>""</c>) el retiro: desde <c>periodoRetiro</c> la factura ya no cobra. Debe estar entre el
    /// segundo mes y el último (para no cobrar ningún mes se anula) y no puede haber pagos de esos meses (422).
    /// </summary>
    public async Task<FacturaDto> RetiroAsync(Guid id, SolicitudRetiro s, CancellationToken ct = default)
    {
        var f = await BuscarAsync(id, ct);
        if (string.IsNullOrEmpty(s.PeriodoRetiro))
        {
            f.PeriodoRetiro = null;
            f.MotivoRetiro = null;
        }
        else
        {
            if (f.Estado != EstadoFactura.Vigente)
            {
                throw new ErrorNegocio($"La factura {f.NumeroFactura} está anulada.", CodigoError.Validacion);
            }
            await AplicarRetiroAsync(Db, f, Periodos.Leer(s.PeriodoRetiro), s.Motivo!.Trim(), ct);
        }
        GenerarDetalle(f);
        await Db.SaveChangesAsync(ct);
        return await ObtenerAsync(id, ct);
    }

    /// <summary>Valida y fija el retiro (no guarda). También lo usan estudiantes y acudientes al inactivarse.</summary>
    public static async Task AplicarRetiroAsync(IBaseDatos db, Factura f, DateOnly retiro, string motivo, CancellationToken ct)
    {
        if (retiro <= f.PeriodoInicio || retiro > f.PeriodoFin)
        {
            throw new ErrorNegocio(
                $"El retiro debe ser entre {Periodos.Nombre(f.PeriodoInicio.AddMonths(1), true)} y {Periodos.Nombre(f.PeriodoFin, true)}. " +
                "Para no cobrar ningún mes, anula la factura.", CodigoError.Validacion);
        }
        var pagados = (await ReglasCuotas.PagosAsync(db, [f.Id], ct)).GetValueOrDefault(f.Id) ?? [];
        if (pagados.Where(p => p.Periodo >= retiro).Select(p => p.Periodo).Distinct().Order().ToList() is { Count: > 0 } meses)
        {
            throw new ErrorNegocio(
                $"La factura {f.NumeroFactura} tiene pagos de {string.Join(", ", meses.Select(m => Periodos.Nombre(m, true)))}. " +
                "Anula esos recibos o elige un mes de retiro posterior.", CodigoError.Validacion);
        }
        f.PeriodoRetiro = retiro;
        f.MotivoRetiro = motivo;
    }

    /// <summary>
    /// Al inactivar un estudiante (o el acudiente): sus facturas vigentes dejan de cobrar desde el mes siguiente (o desde el
    /// mes después del último pagado). Devuelve las facturas ajustadas. No guarda.
    /// </summary>
    public static async Task<int> RetirarAsync(
        IBaseDatos db, Guid? idEstudiante, Guid? idAcudiente, DateOnly hoy, string motivo, CancellationToken ct)
    {
        var siguiente = Periodos.De(hoy).AddMonths(1);
        var facturas = await db.Facturas.Include(f => f.Detalle)
            .Where(f => (idEstudiante == null || f.IdEstudiante == idEstudiante) && (idAcudiente == null || f.IdAcudiente == idAcudiente))
            .Where(f => f.Estado == EstadoFactura.Vigente && f.PeriodoFin >= siguiente
                && (f.PeriodoRetiro == null || f.PeriodoRetiro > siguiente))
            .ToListAsync(ct);
        if (facturas.Count == 0) return 0;
        var pagos = await ReglasCuotas.PagosAsync(db, facturas.Select(f => f.Id).ToList(), ct);
        var ajustadas = 0;
        foreach (var f in facturas)
        {
            var ultimoPagado = (pagos.GetValueOrDefault(f.Id) ?? []).Select(p => p.Periodo).DefaultIfEmpty(DateOnly.MinValue).Max();
            var desde = new[] { siguiente, ultimoPagado == DateOnly.MinValue ? siguiente : ultimoPagado.AddMonths(1), f.PeriodoInicio.AddMonths(1) }.Max();
            if (desde > f.PeriodoFin || (f.PeriodoRetiro is { } actual && actual <= desde)) continue;
            f.PeriodoRetiro = desde;
            f.MotivoRetiro = motivo;
            GenerarDetalle(f);
            ajustadas++;
        }
        return ajustadas;
    }

    public override async Task<string?> MotivoNoEliminarAsync(Guid id, CancellationToken ct = default)
    {
        var recibos = await Db.Pagos.CountAsync(p => p.IdFactura == id, ct);
        if (recibos > 0)
        {
            return $"No se puede eliminar: tiene {recibos} recibo(s) de pago. Anúlala para conservar el historial.";
        }
        return await Db.ProgramacionesCobro.AnyAsync(p => p.IdFactura == id, ct)
            ? "No se puede eliminar: tiene una programación de facturación. Elimina primero la programación o anula la factura."
            : null;
    }
}
