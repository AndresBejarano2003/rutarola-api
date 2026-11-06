using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Domain.Enums;
using RutaRola.Domain.Finanzas;

namespace RutaRola.Application.Finanzas;

/// <summary>Estudiante cotizado: registrado (<c>estudianteId</c>) o solo nombre (cliente nuevo, <c>null</c>).</summary>
public record EstudianteCotizadoDto(Guid? EstudianteId, string Nombre);

/// <summary><c>acudienteId</c> es <c>null</c> cuando el acudiente no está registrado.</summary>
public record CotizacionDto(
    Guid Id, string Numero, DateOnly Fecha, bool Registrados, Guid? AcudienteId, string NombreAcudiente, string TelefonoAcudiente,
    IReadOnlyList<EstudianteCotizadoDto> Estudiantes, decimal ValorPorEstudiante, decimal ValorTotal, EstadoCotizacion Estado,
    string Observaciones, DateTime FechaCreacion, DateTime? FechaActualizacion);

/// <summary><c>numero</c> y <c>valorTotal</c> los calcula el servidor.</summary>
public record SolicitudCotizacion(
    DateOnly? Fecha, bool Registrados, Guid? AcudienteId, string? NombreAcudiente, string? TelefonoAcudiente,
    IReadOnlyList<EstudianteCotizadoDto>? Estudiantes, decimal? ValorPorEstudiante, EstadoCotizacion? Estado, string? Observaciones);

public record FiltrosCotizacion(string? Texto = null, string? Estado = null, DateOnly? Desde = null, DateOnly? Hasta = null);

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
        RuleForEach(x => x.Estudiantes).ChildRules(e => e.RuleFor(x => x.Nombre)
            .NotEmpty().WithMessage("Falta el nombre de un estudiante.").MaximumLength(150).WithMessage("El nombre admite máximo 150 caracteres."));
        RuleFor(x => x.ValorPorEstudiante).NotNull().WithMessage("Ingresa el valor por estudiante.")
            .GreaterThanOrEqualTo(1).WithMessage("El valor por estudiante debe ser mayor que cero.")
            .LessThan(1_000_000_000).WithMessage("El valor es demasiado alto.");
        RuleFor(x => x.Estado).NotNull().WithMessage("Selecciona el estado.");
        RuleFor(x => x.Observaciones).MaximumLength(500).WithMessage("Las observaciones admiten máximo 500 caracteres.");
    }
}

/// <summary>
/// Cotizaciones a acudientes registrados o nuevos: número consecutivo del servidor y
/// <c>valorTotal = valorPorEstudiante × estudiantes</c>. Para registrados, los nombres y el teléfono
/// salen de sus registros (de la empresa; si no, 404).
/// </summary>
public class ServicioCotizaciones(IBaseDatos db) : ServicioCrud<Cotizacion, CotizacionDto, SolicitudCotizacion, FiltrosCotizacion>(db)
{
    protected override string Entidad => "La cotización";
    protected override DbSet<Cotizacion> Conjunto => Db.Cotizaciones;

    protected override IQueryable<Cotizacion> Filtrar(IQueryable<Cotizacion> consulta, FiltrosCotizacion f)
    {
        if (Texto.Opcional(f.Texto) is { } texto)
        {
            var busqueda = Busqueda.Normalizar(texto);
            consulta = consulta.Where(x => FuncionesBd.SinTildes(x.Numero + " " + x.NombreAcudiente).Contains(busqueda)
                || x.Estudiantes.Any(e => FuncionesBd.SinTildes(e.Nombre).Contains(busqueda)));
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

    protected override Task<IReadOnlyList<CotizacionDto>> MapearAsync(IReadOnlyList<Cotizacion> registros, bool detalle, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CotizacionDto>>(registros.Select(c => new CotizacionDto(
            c.Id, c.Numero, c.Fecha, c.IdAcudiente is not null, c.IdAcudiente, c.NombreAcudiente, c.TelefonoAcudiente ?? "",
            c.Estudiantes.OrderBy(e => e.Orden).Select(e => new EstudianteCotizadoDto(e.IdEstudiante, e.Nombre)).ToList(),
            c.ValorPorEstudiante, c.ValorTotal, c.Estado, c.Observaciones ?? "", c.FechaCreacion, c.FechaActualizacion)).ToList());

    protected override async Task AplicarAsync(Cotizacion c, SolicitudCotizacion s, bool nuevo, CancellationToken ct)
    {
        var lista = s.Estudiantes!;
        Domain.Personas.Acudiente? acudiente = null;
        var registrados = new Dictionary<Guid, string>();
        if (s.Registrados)
        {
            acudiente = await Db.Acudientes.FirstOrDefaultAsync(a => a.Id == s.AcudienteId, ct) ?? throw ErrorNegocio.NoEncontrado("El acudiente");
            var ids = lista.Select(e => e.EstudianteId).OfType<Guid>().Distinct().ToList();
            var estudiantes = await Db.Estudiantes.Where(e => ids.Contains(e.Id)).ToListAsync(ct);
            if (estudiantes.Count != ids.Count)
            {
                throw new ErrorNegocio("Uno o más estudiantes no existen o fueron eliminados.", CodigoError.NoEncontrado);
            }
            registrados = estudiantes.ToDictionary(e => e.Id, e => e.NombreCompleto);
        }

        if (nuevo)
        {
            c.Numero = await Consecutivos.SiguienteAsync(Db.Cotizaciones.Select(x => x.Numero), "COT", ct);
        }
        c.Fecha = s.Fecha!.Value;
        c.IdAcudiente = acudiente?.Id;
        c.NombreAcudiente = acudiente?.NombreCompleto ?? s.NombreAcudiente!.Trim();
        c.TelefonoAcudiente = acudiente?.Telefono ?? Texto.Opcional(s.TelefonoAcudiente);
        c.ValorPorEstudiante = Math.Round(s.ValorPorEstudiante!.Value, 2);
        c.ValorTotal = c.ValorPorEstudiante * lista.Count;
        c.Estado = s.Estado!.Value;
        c.Observaciones = Texto.Opcional(s.Observaciones);
        c.Estudiantes.Clear();
        c.Estudiantes.AddRange(lista.Select((e, i) =>
        {
            var id = s.Registrados ? e.EstudianteId : null;
            return new CotizacionEstudiante
            {
                IdEstudiante = id,
                Nombre = id is { } valor ? registrados[valor] : e.Nombre.Trim(),
                Orden = i + 1,
            };
        }));
    }

    public override Task<string?> MotivoNoEliminarAsync(Guid id, CancellationToken ct = default) => Task.FromResult<string?>(null);
}
