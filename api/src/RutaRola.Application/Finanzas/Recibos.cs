using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Domain.Enums;
using RutaRola.Domain.Finanzas;

namespace RutaRola.Application.Finanzas;

/// <summary>Pago de una mensualidad (<c>Recibo</c> del frontend, tabla <c>pagos</c>).</summary>
public record ReciboDto(
    Guid Id, string Numero, Guid FacturaId, string NumeroFactura, string NombreAcudiente, string NombreEstudiante, string Periodo,
    DateOnly FechaPago, decimal Valor, MetodoPago MedioPago, string Referencia, string Estado, string Observaciones,
    DateTime FechaCreacion, DateTime? FechaActualizacion);

/// <summary><c>numero</c> y los datos de la factura los pone el servidor.</summary>
public record SolicitudRecibo(
    Guid? FacturaId, string? Periodo, DateOnly? FechaPago, decimal? Valor, MetodoPago? MedioPago, string? Referencia, string? Estado,
    string? Observaciones);

/// <summary><c>desde</c>/<c>hasta</c>: fecha de pago.</summary>
public record FiltrosRecibo(
    string? Texto = null, Guid? FacturaId = null, string? MedioPago = null, string? Estado = null, DateOnly? Desde = null, DateOnly? Hasta = null);

public class ValidadorSolicitudRecibo : AbstractValidator<SolicitudRecibo>
{
    public ValidadorSolicitudRecibo(TimeProvider reloj)
    {
        RuleFor(x => x.FacturaId).NotEmpty().WithMessage("Selecciona la factura.");
        RuleFor(x => x.Periodo).PeriodoValido("mes que se paga");
        RuleFor(x => x.FechaPago).NotNull().WithMessage("Ingresa la fecha de pago.")
            .Must(f => f is null || f <= Fechas.Hoy(reloj)).WithMessage("La fecha de pago no puede ser futura.");
        RuleFor(x => x.Valor).NotNull().WithMessage("Ingresa el valor pagado.")
            .GreaterThanOrEqualTo(1).WithMessage("El valor debe ser mayor que cero.")
            .LessThan(1_000_000_000).WithMessage("El valor es demasiado alto.");
        RuleFor(x => x.MedioPago).NotNull().WithMessage("Selecciona el medio de pago.");
        RuleFor(x => x.Referencia).MaximumLength(40).WithMessage("La referencia admite máximo 40 caracteres.");
        RuleFor(x => x.Estado).EstadoRegistroValido();
        RuleFor(x => x.Observaciones).MaximumLength(300).WithMessage("Las observaciones admiten máximo 300 caracteres.");
    }
}

/// <summary>
/// Recibos: número consecutivo del servidor; la factura debe estar vigente y el mes dentro de su
/// periodo; un solo recibo válido por factura y mes (409). Anular = estado INACTIVO.
/// </summary>
public class ServicioRecibos(IBaseDatos db) : ServicioCrud<Pago, ReciboDto, SolicitudRecibo, FiltrosRecibo>(db)
{
    protected override string Entidad => "El recibo";
    protected override DbSet<Pago> Conjunto => Db.Pagos;

    protected override IQueryable<Pago> Filtrar(IQueryable<Pago> consulta, FiltrosRecibo f)
    {
        consulta = consulta.Where(x => x.IdFactura != null);
        if (Texto.Opcional(f.Texto) is { } texto)
        {
            var busqueda = Busqueda.Normalizar(texto);
            consulta = consulta.Where(x => FuncionesBd.SinTildes(
                x.Numero + " " + (x.NumeroFactura ?? "") + " " + x.NombreAcudiente + " " + x.NombreEstudiante + " " + (x.NumeroReferencia ?? ""))
                .Contains(busqueda));
        }
        if (f.FacturaId is { } factura)
        {
            consulta = consulta.Where(x => x.IdFactura == factura);
        }
        if (TextoEnum.Leer<MetodoPago>(f.MedioPago) is { } medio)
        {
            consulta = consulta.Where(x => x.MetodoPago == medio);
        }
        if (Texto.Opcional(f.Estado) is { } estado)
        {
            var valor = Estado(estado);
            consulta = consulta.Where(x => x.Estado == valor);
        }
        if (f.Desde is { } desde)
        {
            consulta = consulta.Where(x => x.FechaPago >= desde);
        }
        if (f.Hasta is { } hasta)
        {
            consulta = consulta.Where(x => x.FechaPago <= hasta);
        }
        return consulta;
    }

    protected override IOrderedQueryable<Pago> Ordenar(IQueryable<Pago> consulta) =>
        consulta.OrderByDescending(x => x.FechaPago).ThenByDescending(x => x.Numero.Length).ThenByDescending(x => x.Numero).ThenBy(x => x.Id);

    protected override string Etiqueta(Pago r) => $"{r.Numero} · {r.NombreEstudiante} · {Periodos.Nombre(r.Periodo ?? r.FechaPago)}";

    public static EstadoPago Estado(string? estado) => EstadoRegistro.EsActivo(estado) ? EstadoPago.Vigente : EstadoPago.Anulado;

    public static ReciboDto Mapear(Pago r) => new(
        r.Id, r.Numero, r.IdFactura ?? Guid.Empty, r.NumeroFactura ?? "", r.NombreAcudiente, r.NombreEstudiante, Periodos.Texto(r.Periodo),
        r.FechaPago, r.Valor, r.MetodoPago, r.NumeroReferencia ?? "", EstadoRegistro.Texto(r.Estado == EstadoPago.Vigente),
        r.Observaciones ?? "", r.FechaCreacion, r.FechaActualizacion);

    protected override Task<IReadOnlyList<ReciboDto>> MapearAsync(IReadOnlyList<Pago> registros, bool detalle, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ReciboDto>>(registros.Select(Mapear).ToList());

    protected override async Task AplicarAsync(Pago r, SolicitudRecibo s, bool nuevo, CancellationToken ct)
    {
        var factura = await Db.Facturas.FirstOrDefaultAsync(f => f.Id == s.FacturaId, ct) ?? throw ErrorNegocio.NoEncontrado("La factura");
        var periodo = Periodos.Leer(s.Periodo!);
        var estado = Estado(s.Estado);
        if (estado == EstadoPago.Vigente && factura.Estado != EstadoFactura.Vigente)
        {
            throw new ErrorNegocio($"La factura {factura.NumeroFactura} está anulada.", CodigoError.Validacion);
        }
        if (periodo < factura.PeriodoInicio || periodo > factura.PeriodoFin)
        {
            throw new ErrorNegocio(
                $"{Periodos.Nombre(periodo, true)} no está dentro del periodo de la factura {factura.NumeroFactura}.", CodigoError.Validacion);
        }
        if (estado == EstadoPago.Vigente)
        {
            var idActual = nuevo ? (Guid?)null : r.Id;
            var repetido = await Db.Pagos.AsNoTracking()
                .Where(x => x.Id != idActual && x.Estado == EstadoPago.Vigente && x.IdFactura == factura.Id && x.Periodo == periodo)
                .Select(x => x.Numero).FirstOrDefaultAsync(ct);
            if (repetido is not null)
            {
                throw new ErrorNegocio(
                    $"{Periodos.Nombre(periodo, true)} de la factura {factura.NumeroFactura} ya está pagado (recibo {repetido}).", CodigoError.Duplicado);
            }
        }

        if (nuevo)
        {
            r.Numero = await Consecutivos.SiguienteAsync(Db.Pagos.Select(x => x.Numero), "REC", ct);
        }
        // Snapshot de la factura al momento del pago.
        if (nuevo || r.IdFactura != factura.Id)
        {
            r.NumeroFactura = factura.NumeroFactura;
            r.NombreAcudiente = factura.NombreAcudiente;
            r.NombreEstudiante = factura.NombreEstudiante;
        }
        r.IdFactura = factura.Id;
        r.IdEstudiante = factura.IdEstudiante;
        r.Periodo = periodo;
        r.FechaPago = s.FechaPago!.Value;
        r.Valor = Math.Round(s.Valor!.Value, 2);
        r.MetodoPago = s.MedioPago!.Value;
        r.NumeroReferencia = Texto.Opcional(s.Referencia);
        r.Estado = estado;
        r.Observaciones = Texto.Opcional(s.Observaciones);
    }

    /// <summary>Como el modo local: un recibo se puede eliminar (queda en la auditoría); lo usual es anularlo.</summary>
    public override Task<string?> MotivoNoEliminarAsync(Guid id, CancellationToken ct = default) => Task.FromResult<string?>(null);
}
