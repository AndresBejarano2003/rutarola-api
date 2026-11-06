using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Domain.Enums;
using RutaRola.Domain.Finanzas;

namespace RutaRola.Application.Finanzas;

/// <summary>
/// Contrato mensual (<c>Factura</c> del frontend): <c>valorContratacion</c> es el valor MENSUAL y los
/// periodos van como <c>YYYY-MM</c>. Los nombres son los del momento de emitir.
/// </summary>
public record FacturaDto(
    Guid Id, string Numero, DateOnly FechaEmision, Guid AcudienteId, Guid EstudianteId, string NombreAcudiente,
    string NombreEstudiante, decimal ValorContratacion, string PeriodoInicio, string PeriodoFin, string Concepto, string Estado,
    string Observaciones, DateTime FechaCreacion, DateTime? FechaActualizacion);

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
public class ServicioFacturas(IBaseDatos db) : ServicioCrud<Factura, FacturaDto, SolicitudFactura, FiltrosFactura>(db)
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

    public static FacturaDto Mapear(Factura f) => new(
        f.Id, f.NumeroFactura, f.FechaEmision, f.IdAcudiente, f.IdEstudiante, f.NombreAcudiente, f.NombreEstudiante, f.ValorMensual,
        Periodos.Texto(f.PeriodoInicio), Periodos.Texto(f.PeriodoFin), f.Concepto, EstadoRegistro.Texto(f.Estado == EstadoFactura.Vigente),
        f.Observaciones ?? "", f.FechaCreacion, f.FechaActualizacion);

    protected override Task<IReadOnlyList<FacturaDto>> MapearAsync(IReadOnlyList<Factura> registros, bool detalle, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<FacturaDto>>(registros.Select(Mapear).ToList());

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

        // Una línea por mes.
        var meses = Periodos.Entre(inicio, fin).ToList();
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
