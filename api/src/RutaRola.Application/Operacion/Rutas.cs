using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Domain.Operacion;

namespace RutaRola.Application.Operacion;

/// <summary><c>estudianteIds</c> en el orden de recogida (la entrega lo recorre al revés).</summary>
public record RutaDto(
    Guid Id, string Codigo, string Nombre, Guid ColegioId, string Jornada, string Sector, IReadOnlyList<Guid> EstudianteIds,
    string Estado, string Observaciones, DateTime FechaCreacion, DateTime? FechaActualizacion);

public record SolicitudRuta(
    string? Codigo, string? Nombre, Guid? ColegioId, string? Jornada, string? Sector, IReadOnlyList<Guid>? EstudianteIds,
    string? Estado, string? Observaciones);

/// <summary><c>texto</c> busca en código, nombre y sector; <c>estudianteId</c> = rutas que llevan a ese estudiante.</summary>
public record FiltrosRuta(string? Texto = null, Guid? ColegioId = null, string? Jornada = null, string? Estado = null, Guid? EstudianteId = null);

public class ValidadorSolicitudRuta : AbstractValidator<SolicitudRuta>
{
    public ValidadorSolicitudRuta()
    {
        RuleFor(x => x.Codigo).NotEmpty().WithMessage("Ingresa el código de la ruta.")
            .Matches("^[A-Za-z0-9-]{2,10}$").WithMessage("El código admite de 2 a 10 letras, números o guiones (ej. R-01).");
        RuleFor(x => x.Nombre).NotEmpty().WithMessage("Ingresa el nombre de la ruta.")
            .MaximumLength(60).WithMessage("El nombre admite máximo 60 caracteres.");
        RuleFor(x => x.ColegioId).NotEmpty().WithMessage("Selecciona el colegio.");
        RuleFor(x => x.Jornada).NotEmpty().WithMessage("Selecciona la jornada.")
            .Must(j => j is null || Catalogos.Jornadas.Contains(j)).WithMessage("La jornada no es válida.");
        RuleFor(x => x.Sector).NotEmpty().WithMessage("Ingresa los barrios o sectores de la ruta.")
            .MaximumLength(200).WithMessage("El sector admite máximo 200 caracteres.");
        RuleFor(x => x.EstudianteIds).Must(ids => ids is null || ids.Distinct().Count() == ids.Count)
            .WithMessage("Un estudiante aparece dos veces en la ruta.");
        RuleFor(x => x.Estado).EstadoRegistroValido();
        RuleFor(x => x.Observaciones).MaximumLength(500).WithMessage("Las observaciones admiten máximo 500 caracteres.");
    }
}

/// <summary>
/// Rutas y sus estudiantes en orden de recogida. Código único; los estudiantes deben ser del colegio de
/// la ruta y no pueden estar en otra ruta activa de la misma jornada; no se elimina con asignaciones.
/// </summary>
public class ServicioRutas(IBaseDatos db) : ServicioCrud<Ruta, RutaDto, SolicitudRuta, FiltrosRuta>(db)
{
    protected override string Entidad => "La ruta";
    protected override DbSet<Ruta> Conjunto => Db.Rutas;

    /// <summary><c>R-01 · Norte</c> (<c>etiquetaRuta</c>).</summary>
    public static string Etiquetar(Ruta r) => $"{r.Codigo} · {r.Nombre}";

    protected override IQueryable<Ruta> Filtrar(IQueryable<Ruta> consulta, FiltrosRuta f)
    {
        if (Texto.Opcional(f.Texto) is { } texto)
        {
            var busqueda = Busqueda.Normalizar(texto);
            consulta = consulta.Where(r => FuncionesBd.SinTildes(r.Codigo + " " + r.Nombre + " " + (r.Sector ?? "")).Contains(busqueda));
        }
        if (f.ColegioId is { } colegio)
        {
            consulta = consulta.Where(r => r.IdColegio == colegio);
        }
        if (Texto.Opcional(f.Jornada) is { } jornada)
        {
            consulta = consulta.Where(r => r.Jornada == jornada);
        }
        if (Texto.Opcional(f.Estado) is { } estado)
        {
            var activo = EstadoRegistro.EsActivo(estado);
            consulta = consulta.Where(r => r.Activo == activo);
        }
        if (f.EstudianteId is { } estudiante)
        {
            consulta = consulta.Where(r => r.Estudiantes.Any(e => e.IdEstudiante == estudiante));
        }
        return consulta;
    }

    /// <summary>Código en orden "natural" (R-2 antes de R-10).</summary>
    protected override IOrderedQueryable<Ruta> Ordenar(IQueryable<Ruta> consulta) =>
        consulta.OrderBy(r => r.Codigo.Length).ThenBy(r => r.Codigo).ThenBy(r => r.Id);

    protected override IQueryable<Ruta> Incluir(IQueryable<Ruta> consulta) => consulta.Include(r => r.Estudiantes);

    protected override string Etiqueta(Ruta r) => Etiquetar(r);

    protected override Task<IReadOnlyList<RutaDto>> MapearAsync(IReadOnlyList<Ruta> registros, bool detalle, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<RutaDto>>(registros.Select(r => new RutaDto(
            r.Id, r.Codigo, r.Nombre, r.IdColegio, r.Jornada, r.Sector ?? "",
            r.Estudiantes.OrderBy(e => e.OrdenRecogida).Select(e => e.IdEstudiante).ToList(),
            EstadoRegistro.Texto(r.Activo), r.Observaciones ?? "", r.FechaCreacion, r.FechaActualizacion)).ToList());

    protected override async Task AplicarAsync(Ruta r, SolicitudRuta s, bool nuevo, CancellationToken ct)
    {
        var codigo = s.Codigo!.Trim();
        var codigoMayus = codigo.ToUpperInvariant();
        var idActual = nuevo ? (Guid?)null : r.Id;
        if (await Db.Rutas.AnyAsync(x => x.Id != idActual && x.Codigo.ToUpper() == codigoMayus, ct))
        {
            throw new ErrorNegocio($"Ya existe una ruta con el código {codigo}.", CodigoError.Duplicado);
        }

        var colegio = await Db.Colegios.FirstOrDefaultAsync(c => c.Id == s.ColegioId, ct) ?? throw ErrorNegocio.NoEncontrado("El colegio");
        var ids = s.EstudianteIds ?? [];
        var estudiantes = await Db.Estudiantes.Where(e => ids.Contains(e.Id)).Select(e => new { e.Id, e.IdColegio }).ToListAsync(ct);
        if (estudiantes.Count != ids.Count)
        {
            throw new ErrorNegocio("Uno o más estudiantes no existen o fueron eliminados.", CodigoError.NoEncontrado);
        }
        if (estudiantes.Any(e => e.IdColegio != colegio.Id))
        {
            throw new ErrorNegocio($"Todos los estudiantes de la ruta deben ser del {colegio.RazonSocial}.", CodigoError.Validacion);
        }

        var jornada = s.Jornada!.Trim();
        var activa = EstadoRegistro.EsActivo(s.Estado);
        if (activa && ids.Count > 0)
        {
            // Un estudiante no puede estar en dos rutas activas de la misma jornada.
            var choque = await Db.Rutas
                .Where(x => x.Id != idActual && x.Activo && x.Jornada == jornada && x.Estudiantes.Any(e => ids.Contains(e.IdEstudiante)))
                .FirstOrDefaultAsync(ct);
            if (choque is not null)
            {
                throw new ErrorNegocio(
                    $"Uno o más estudiantes ya están en la ruta {Etiquetar(choque)} (jornada {choque.Jornada}). Quítalos de esa ruta primero.",
                    CodigoError.Validacion);
            }
        }

        r.Codigo = codigo;
        r.Nombre = s.Nombre!.Trim();
        r.IdColegio = colegio.Id;
        r.Jornada = jornada;
        r.Sector = s.Sector!.Trim();
        r.Activo = activa;
        r.Observaciones = Texto.Opcional(s.Observaciones);

        r.Estudiantes.RemoveAll(e => !ids.Contains(e.IdEstudiante));
        for (var i = 0; i < ids.Count; i++)
        {
            var enRuta = r.Estudiantes.FirstOrDefault(e => e.IdEstudiante == ids[i]);
            if (enRuta is null)
            {
                enRuta = new RutaEstudiante { IdEstudiante = ids[i] };
                r.Estudiantes.Add(enRuta);
            }
            enRuta.OrdenRecogida = i + 1;
        }
    }

    public override async Task<string?> MotivoNoEliminarAsync(Guid id, CancellationToken ct = default)
    {
        var total = await Db.AsignacionesRuta.CountAsync(a => a.IdRuta == id, ct);
        return total > 0
            ? $"No se puede eliminar: la ruta tiene {total} asignación(es) de vehículo y conductor. Cámbiala a Inactiva."
            : null;
    }
}
