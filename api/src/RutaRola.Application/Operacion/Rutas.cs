using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Application.Finanzas;
using RutaRola.Domain.Operacion;

namespace RutaRola.Application.Operacion;

/// <summary>
/// <c>colegioIds</c>: los colegios que atiende (todos de la jornada), en orden; <c>colegioId</c> = el primero (lo leen
/// las versiones anteriores de la app). <c>estudianteIds</c> en el orden de recogida (la entrega lo recorre al revés).
/// </summary>
public record RutaDto(
    Guid Id, string Codigo, string Nombre, Guid ColegioId, IReadOnlyList<Guid> ColegioIds, string Jornada, string Sector,
    IReadOnlyList<Guid> EstudianteIds, string Estado, string Observaciones, DateTime FechaCreacion, DateTime? FechaActualizacion);

/// <summary>
/// <c>codigo</c> lo genera el servidor al crear (<c>R-01</c>, <c>R-02</c>…) y no cambia: el que llegue se ignora.
/// <c>colegioIds</c> (uno o varios); sin él vale <c>colegioId</c> (versiones anteriores de la app).
/// </summary>
public record SolicitudRuta(
    string? Codigo, string? Nombre, Guid? ColegioId, string? Jornada, string? Sector, IReadOnlyList<Guid>? EstudianteIds,
    string? Estado, string? Observaciones, IReadOnlyList<Guid>? ColegioIds = null)
{
    /// <summary>Los colegios pedidos: <c>colegioIds</c> o, si no llega, <c>colegioId</c>.</summary>
    public IReadOnlyList<Guid> Colegios => ColegioIds is { Count: > 0 } ids ? ids : ColegioId is { } id && id != Guid.Empty ? [id] : [];
}

/// <summary><c>texto</c> busca en código, nombre y sector; <c>estudianteId</c> = rutas que llevan a ese estudiante.</summary>
public record FiltrosRuta(string? Texto = null, Guid? ColegioId = null, string? Jornada = null, string? Estado = null, Guid? EstudianteId = null);

public class ValidadorSolicitudRuta : AbstractValidator<SolicitudRuta>
{
    public ValidadorSolicitudRuta()
    {
        RuleFor(x => x.Nombre).NotEmpty().WithMessage("Ingresa el nombre de la ruta.")
            .MaximumLength(60).WithMessage("El nombre admite máximo 60 caracteres.");
        RuleFor(x => x.Colegios).NotEmpty().WithMessage("Agrega al menos un colegio a la ruta.")
            .Must(ids => ids.Distinct().Count() == ids.Count).WithMessage("Un colegio aparece dos veces en la ruta.")
            .Must(ids => ids.Count <= 20).WithMessage("Una ruta admite máximo 20 colegios.")
            .OverridePropertyName("colegioIds");
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
/// Rutas, sus colegios y sus estudiantes en orden de recogida. El código es un consecutivo por empresa que asigna el
/// servidor (<see cref="PrefijoCodigo"/>); una ruta puede atender varios colegios de su jornada (cada colegio nuevo debe
/// tener cursos activos en ella); los estudiantes deben ser de esos colegios y de la jornada de la ruta (los que ya
/// estaban se conservan aunque su jornada cambie) y no pueden estar en otra ruta activa de la misma jornada; no se
/// elimina con asignaciones. Si el administrador cambia el orden de recogida, los recorridos EN CURSO de la ruta
/// reordenan lo que les falta por visitar y el personal lo ve al instante.
/// </summary>
public class ServicioRutas(IBaseDatos db, ServicioRecorridos recorridos) : ServicioCrud<Ruta, RutaDto, SolicitudRuta, FiltrosRuta>(db)
{
    /// <summary>Nuevo orden de recogida guardado en esta petición (para los recorridos en curso).</summary>
    private IReadOnlyList<Guid>? ordenCambiado;

    protected override string Entidad => "La ruta";
    protected override DbSet<Ruta> Conjunto => Db.Rutas;

    /// <summary>Consecutivo de las rutas: <c>R-01</c>, <c>R-02</c>… (sigue tras el mayor existente, también los anteriores).</summary>
    public const string PrefijoCodigo = "R";

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
            consulta = consulta.Where(r => r.Colegios.Any(x => x.IdColegio == colegio));
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

    protected override IQueryable<Ruta> Incluir(IQueryable<Ruta> consulta) => consulta.Include(r => r.Estudiantes).Include(r => r.Colegios);

    protected override string Etiqueta(Ruta r) => Etiquetar(r);

    protected override Task<IReadOnlyList<RutaDto>> MapearAsync(IReadOnlyList<Ruta> registros, bool detalle, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<RutaDto>>(registros.Select(r =>
        {
            var colegios = ColegiosEnOrden(r);
            return new RutaDto(
            r.Id, r.Codigo, r.Nombre, colegios.FirstOrDefault(), colegios, r.Jornada, r.Sector ?? "",
            r.Estudiantes.OrderBy(e => e.OrdenRecogida).Select(e => e.IdEstudiante).ToList(),
            EstadoRegistro.Texto(r.Activo), r.Observaciones ?? "", r.FechaCreacion, r.FechaActualizacion);
        }).ToList());

    /// <summary>Ids de los colegios de la ruta en su orden.</summary>
    public static IReadOnlyList<Guid> ColegiosEnOrden(Ruta r) => r.Colegios.OrderBy(c => c.Orden).Select(c => c.IdColegio).ToList();

    protected override async Task AplicarAsync(Ruta r, SolicitudRuta s, bool nuevo, CancellationToken ct)
    {
        var idActual = nuevo ? (Guid?)null : r.Id;
        var jornada = s.Jornada!.Trim();
        var idsColegio = s.Colegios;
        var colegios = await Db.Colegios.Include(c => c.Cursos).Where(c => idsColegio.Contains(c.Id)).ToListAsync(ct);
        if (colegios.Count != idsColegio.Count)
        {
            throw ErrorNegocio.NoEncontrado("El colegio");
        }
        // Un colegio que se agrega debe tener cursos en la jornada de la ruta (los que ya estaban se conservan).
        var sinJornada = colegios
            .Where(c => r.Colegios.All(x => x.IdColegio != c.Id) && !c.Cursos.Any(x => x.Activo && x.Jornada == jornada))
            .Select(c => c.RazonSocial).ToList();
        if (sinJornada.Count > 0)
        {
            throw new ErrorNegocio(
                $"{string.Join(", ", sinJornada)} no {(sinJornada.Count == 1 ? "tiene" : "tienen")} cursos activos en la jornada {jornada}.",
                CodigoError.Validacion);
        }
        var ids = s.EstudianteIds ?? [];
        var estudiantes = await Db.Estudiantes.Where(e => ids.Contains(e.Id))
            .Select(e => new { e.Id, e.IdColegio, e.Jornada, e.PrimerNombre, e.PrimerApellido }).ToListAsync(ct);
        if (estudiantes.Count != ids.Count)
        {
            throw new ErrorNegocio("Uno o más estudiantes no existen o fueron eliminados.", CodigoError.NoEncontrado);
        }
        if (estudiantes.Any(e => !idsColegio.Contains(e.IdColegio)))
        {
            var nombres = idsColegio.Select(id => colegios.First(c => c.Id == id).RazonSocial).ToList();
            throw new ErrorNegocio(
                nombres.Count == 1
                    ? $"Todos los estudiantes de la ruta deben ser del {nombres[0]}."
                    : $"Todos los estudiantes de la ruta deben ser de sus colegios: {string.Join(", ", nombres)}.",
                CodigoError.Validacion);
        }

        // Solo estudiantes de la jornada de la ruta (los que ya iban en ella se conservan).
        var otraJornada = estudiantes
            .Where(e => e.Jornada != jornada && r.Estudiantes.All(x => x.IdEstudiante != e.Id))
            .Select(e => $"{e.PrimerNombre} {e.PrimerApellido} ({e.Jornada})").ToList();
        if (otraJornada.Count > 0)
        {
            throw new ErrorNegocio(
                $"La ruta es de la jornada {jornada} y estos estudiantes son de otra: {string.Join(", ", otraJornada)}.",
                CodigoError.Validacion);
        }

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

        if (nuevo)
        {
            r.Codigo = await Consecutivos.SiguienteAsync(Db.Rutas.Select(x => x.Codigo), PrefijoCodigo, ct, digitos: 2);
        }
        r.Nombre = s.Nombre!.Trim();
        r.Jornada = jornada;
        r.Sector = s.Sector!.Trim();
        r.Activo = activa;
        r.Observaciones = Texto.Opcional(s.Observaciones);

        r.Colegios.RemoveAll(c => !idsColegio.Contains(c.IdColegio));
        for (var i = 0; i < idsColegio.Count; i++)
        {
            var enRuta = r.Colegios.FirstOrDefault(c => c.IdColegio == idsColegio[i]);
            if (enRuta is null)
            {
                enRuta = new RutaColegio { IdColegio = idsColegio[i] };
                r.Colegios.Add(enRuta);
            }
            enRuta.Orden = i + 1;
        }

        var ordenAnterior = r.Estudiantes.OrderBy(e => e.OrdenRecogida).Select(e => e.IdEstudiante).ToList();
        ordenCambiado = !nuevo && !ordenAnterior.SequenceEqual(ids) ? ids : null;
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

    /// <summary>El orden de recogida cambió: los recorridos en curso de la ruta reordenan lo que falta (aviso en tiempo real).</summary>
    protected override async Task DespuesDeGuardarAsync(Ruta r, bool nuevo, CancellationToken ct)
    {
        if (ordenCambiado is { } orden)
        {
            ordenCambiado = null;
            await recorridos.AplicarOrdenDeRutaAsync(r.Id, orden, ct);
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
