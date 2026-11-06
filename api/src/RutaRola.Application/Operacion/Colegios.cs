using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Domain.Operacion;

namespace RutaRola.Application.Operacion;

public record CursoDto(string Grado, TimeOnly HoraEntrada, TimeOnly HoraSalida, string Estado);

public record ColegioDto(
    Guid Id, string Nit, string RazonSocial, string Direccion, string Telefono, IReadOnlyList<CursoDto> Cursos,
    string Estado, DateTime FechaCreacion, DateTime? FechaActualizacion);

public record SolicitudCurso(string? Grado, TimeOnly? HoraEntrada, TimeOnly? HoraSalida, string? Estado);

public record SolicitudColegio(
    string? Nit, string? RazonSocial, string? Direccion, string? Telefono, IReadOnlyList<SolicitudCurso>? Cursos, string? Estado);

/// <summary><c>texto</c> busca en razón social y NIT.</summary>
public record FiltrosColegio(string? Texto = null, string? Estado = null);

public class ValidadorSolicitudColegio : AbstractValidator<SolicitudColegio>
{
    public ValidadorSolicitudColegio()
    {
        RuleFor(x => x.Nit).NotEmpty().WithMessage("Ingresa el NIT.")
            .Matches(ReglasComunes.PatronNit).WithMessage("Ingresa un NIT válido (ej. 900123456-7).");
        RuleFor(x => x.RazonSocial).NotEmpty().WithMessage("Ingresa la razón social.")
            .MaximumLength(120).WithMessage("La razón social admite máximo 120 caracteres.");
        RuleFor(x => x.Direccion).NotEmpty().WithMessage("Ingresa la dirección.")
            .MaximumLength(120).WithMessage("La dirección admite máximo 120 caracteres.");
        RuleFor(x => x.Telefono).TelefonoValido();
        RuleFor(x => x.Estado).EstadoRegistroValido();
        RuleForEach(x => x.Cursos).ChildRules(c =>
        {
            c.RuleFor(x => x.Grado).Must(g => Catalogos.Grados.Contains(g)).WithMessage("El grado del curso no es válido.");
            c.RuleFor(x => x.HoraEntrada).NotNull().WithMessage("Ingresa la hora de entrada de cada curso.");
            c.RuleFor(x => x.HoraSalida).NotNull().WithMessage("Ingresa la hora de salida de cada curso.");
            c.RuleFor(x => x.HoraSalida).GreaterThan(x => x.HoraEntrada)
                .When(x => x.HoraEntrada is not null && x.HoraSalida is not null)
                .WithMessage(x => $"En {x.Grado} la hora de salida debe ser posterior a la de entrada.");
            c.RuleFor(x => x.Estado).EstadoRegistroValido();
        });
        RuleFor(x => x.Cursos).Must(c => c is null || c.Select(x => x.Grado).Distinct().Count() == c.Count)
            .WithMessage("Cada grado puede aparecer una sola vez en los cursos.");
    }
}

/// <summary>
/// Colegios y sus cursos. NIT único por empresa (comparando sin el dígito de verificación); no se
/// elimina si tiene estudiantes o rutas.
/// </summary>
public class ServicioColegios(IBaseDatos db) : ServicioCrud<Colegio, ColegioDto, SolicitudColegio, FiltrosColegio>(db)
{
    protected override string Entidad => "El colegio";
    protected override DbSet<Colegio> Conjunto => Db.Colegios;

    protected override IQueryable<Colegio> Filtrar(IQueryable<Colegio> consulta, FiltrosColegio f)
    {
        if (Texto.Opcional(f.Texto) is { } texto)
        {
            var busqueda = Busqueda.Normalizar(texto);
            consulta = consulta.Where(c => FuncionesBd.SinTildes(c.RazonSocial + " " + c.Nit).Contains(busqueda));
        }
        if (Texto.Opcional(f.Estado) is { } estado)
        {
            var activo = EstadoRegistro.EsActivo(estado);
            consulta = consulta.Where(c => c.Activo == activo);
        }
        return consulta;
    }

    protected override IOrderedQueryable<Colegio> Ordenar(IQueryable<Colegio> consulta) =>
        consulta.OrderBy(c => FuncionesBd.SinTildes(c.RazonSocial)).ThenBy(c => c.Id);

    protected override IQueryable<Colegio> Incluir(IQueryable<Colegio> consulta) => consulta.Include(c => c.Cursos);

    protected override string Etiqueta(Colegio c) => c.RazonSocial;

    protected override Task<IReadOnlyList<ColegioDto>> MapearAsync(IReadOnlyList<Colegio> registros, bool detalle, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ColegioDto>>(registros.Select(c => new ColegioDto(
            c.Id, c.Nit, c.RazonSocial, c.Direccion, c.Telefono ?? "",
            c.Cursos.OrderBy(x => Catalogos.OrdenGrado(x.Grado))
                .Select(x => new CursoDto(x.Grado, x.HoraEntrada, x.HoraSalida, EstadoRegistro.Texto(x.Activo))).ToList(),
            EstadoRegistro.Texto(c.Activo), c.FechaCreacion, c.FechaActualizacion)).ToList());

    protected override async Task AplicarAsync(Colegio c, SolicitudColegio s, bool nuevo, CancellationToken ct)
    {
        var nit = s.Nit!.Trim();
        var baseNit = nit.Split('-')[0];
        var idActual = nuevo ? (Guid?)null : c.Id;
        if (await Db.Colegios.AnyAsync(x => x.Id != idActual && (x.Nit == baseNit || x.Nit.StartsWith(baseNit + "-")), ct))
        {
            throw new ErrorNegocio($"Ya existe un colegio con el NIT {nit}.", CodigoError.Duplicado);
        }

        c.Nit = nit;
        c.RazonSocial = s.RazonSocial!.Trim();
        c.Direccion = s.Direccion!.Trim();
        c.Telefono = Texto.Opcional(s.Telefono);
        c.Activo = EstadoRegistro.EsActivo(s.Estado);

        // Cursos: se actualizan por grado; los que no vienen se quitan.
        var solicitados = s.Cursos ?? [];
        c.Cursos.RemoveAll(x => solicitados.All(n => n.Grado != x.Grado));
        foreach (var curso in solicitados)
        {
            var existente = c.Cursos.FirstOrDefault(x => x.Grado == curso.Grado);
            if (existente is null)
            {
                existente = new ColegioCurso { Grado = curso.Grado! };
                c.Cursos.Add(existente);
            }
            existente.HoraEntrada = curso.HoraEntrada!.Value;
            existente.HoraSalida = curso.HoraSalida!.Value;
            existente.Activo = EstadoRegistro.EsActivo(curso.Estado);
        }
    }

    public override async Task<string?> MotivoNoEliminarAsync(Guid id, CancellationToken ct = default)
    {
        var estudiantes = await Db.Estudiantes.CountAsync(e => e.IdColegio == id, ct);
        var rutas = await Db.Rutas.CountAsync(r => r.IdColegio == id, ct);
        var usos = new[]
        {
            estudiantes > 0 ? $"{estudiantes} estudiante(s)" : "",
            rutas > 0 ? $"{rutas} ruta(s)" : "",
        }.Where(u => u.Length > 0).ToList();
        return usos.Count > 0
            ? $"No se puede eliminar: el colegio tiene {string.Join(" y ", usos)}. Reasígnalos o cambia el colegio a Inactivo."
            : null;
    }
}
