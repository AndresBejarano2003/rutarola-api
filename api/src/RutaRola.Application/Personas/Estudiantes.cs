using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Domain.Personas;

namespace RutaRola.Application.Personas;

/// <summary><c>acudienteId</c> = el acudiente principal (en la base de datos la relación es N:M).</summary>
public record EstudianteDto(
    Guid Id, string TipoDocumento, string NumeroDocumento, string PrimerNombre, string SegundoNombre, string PrimerApellido,
    string SegundoApellido, DateOnly FechaNacimiento, string Direccion, Guid? AcudienteId, Guid ColegioId, string Grado,
    string Jornada, string Estado, string Observaciones, DateTime FechaCreacion, DateTime? FechaActualizacion);

public record SolicitudEstudiante(
    string? TipoDocumento, string? NumeroDocumento, string? PrimerNombre, string? SegundoNombre, string? PrimerApellido,
    string? SegundoApellido, DateOnly? FechaNacimiento, string? Direccion, Guid? AcudienteId, Guid? ColegioId, string? Grado,
    string? Jornada, string? Estado, string? Observaciones) : ISolicitudPersona;

/// <summary><c>acudienteId</c> encuentra a los estudiantes de ese acudiente (principal o no).</summary>
public record FiltrosEstudiante(
    string? Texto = null, Guid? AcudienteId = null, Guid? ColegioId = null, string? Grado = null, string? Estado = null);

public class ValidadorSolicitudEstudiante : AbstractValidator<SolicitudEstudiante>
{
    public ValidadorSolicitudEstudiante()
    {
        this.Persona();
        RuleFor(x => x.FechaNacimiento).FechaNoFutura("fecha de nacimiento");
        RuleFor(x => x.Direccion).NotEmpty().WithMessage("Ingresa la dirección.")
            .MaximumLength(120).WithMessage("La dirección admite máximo 120 caracteres.");
        RuleFor(x => x.AcudienteId).NotEmpty().WithMessage("Selecciona el acudiente.");
        RuleFor(x => x.ColegioId).NotEmpty().WithMessage("Selecciona el colegio.");
        RuleFor(x => x.Grado).NotEmpty().WithMessage("Selecciona el grado.")
            .Must(g => g is null || Catalogos.Grados.Contains(g)).WithMessage("El grado no es válido.");
        RuleFor(x => x.Jornada).NotEmpty().WithMessage("Selecciona la jornada.")
            .Must(j => j is null || Catalogos.Jornadas.Contains(j)).WithMessage("La jornada no es válida.");
        RuleFor(x => x.Estado).EstadoRegistroValido();
        RuleFor(x => x.Observaciones).MaximumLength(500).WithMessage("Las observaciones admiten máximo 500 caracteres.");
    }
}

/// <summary>
/// Estudiantes (datos de menores: solo el administrador de la empresa). El colegio y el acudiente
/// deben ser de la misma empresa (404 si no); el grado debe ser un curso activo del colegio; no se
/// elimina si está en una ruta o tiene facturas.
/// </summary>
public class ServicioEstudiantes(IBaseDatos db) : ServicioCrud<Estudiante, EstudianteDto, SolicitudEstudiante, FiltrosEstudiante>(db)
{
    protected override string Entidad => "El estudiante";
    protected override DbSet<Estudiante> Conjunto => Db.Estudiantes;

    protected override IQueryable<Estudiante> Filtrar(IQueryable<Estudiante> consulta, FiltrosEstudiante f)
    {
        consulta = consulta.BuscarPersona(f.Texto);
        if (f.AcudienteId is { } acudiente)
        {
            consulta = consulta.Where(e => e.Acudientes.Any(a => a.IdAcudiente == acudiente));
        }
        if (f.ColegioId is { } colegio)
        {
            consulta = consulta.Where(e => e.IdColegio == colegio);
        }
        if (Texto.Opcional(f.Grado) is { } grado)
        {
            consulta = consulta.Where(e => e.Grado == grado);
        }
        if (Texto.Opcional(f.Estado) is { } estado)
        {
            var activo = EstadoRegistro.EsActivo(estado);
            consulta = consulta.Where(e => e.Activo == activo);
        }
        return consulta;
    }

    protected override IOrderedQueryable<Estudiante> Ordenar(IQueryable<Estudiante> consulta) => consulta.OrdenarPorApellido();

    protected override IQueryable<Estudiante> Incluir(IQueryable<Estudiante> consulta) => consulta.Include(e => e.Acudientes);

    protected override string Etiqueta(Estudiante e) => e.NombreCompleto;

    protected override Task<IReadOnlyList<EstudianteDto>> MapearAsync(IReadOnlyList<Estudiante> registros, bool detalle, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<EstudianteDto>>(registros.Select(e => new EstudianteDto(
            e.Id, e.TipoDocumento, e.NumeroDocumento, e.PrimerNombre, e.SegundoNombre ?? "", e.PrimerApellido,
            e.SegundoApellido ?? "", e.FechaNacimiento, e.Direccion,
            e.Acudientes.FirstOrDefault(a => a.EsPrincipal)?.IdAcudiente ?? e.Acudientes.FirstOrDefault()?.IdAcudiente,
            e.IdColegio, e.Grado, e.Jornada, EstadoRegistro.Texto(e.Activo), e.Observaciones ?? "",
            e.FechaCreacion, e.FechaActualizacion)).ToList());

    protected override async Task AplicarAsync(Estudiante e, SolicitudEstudiante s, bool nuevo, CancellationToken ct)
    {
        await Db.Estudiantes.ValidarDocumentoUnicoAsync(s, nuevo ? null : e.Id, "un estudiante", ct);

        // Las referencias se buscan con el filtro de empresa: un id de otra empresa no existe.
        var colegio = await Db.Colegios.Include(c => c.Cursos).FirstOrDefaultAsync(c => c.Id == s.ColegioId, ct)
            ?? throw ErrorNegocio.NoEncontrado("El colegio");
        if (!await Db.Acudientes.AnyAsync(a => a.Id == s.AcudienteId, ct))
        {
            throw ErrorNegocio.NoEncontrado("El acudiente");
        }
        var grado = s.Grado!.Trim();
        var cambioCurso = nuevo || e.IdColegio != colegio.Id || e.Grado != grado;
        if (cambioCurso && !colegio.Cursos.Any(c => c.Grado == grado && c.Activo))
        {
            throw new ErrorNegocio($"El colegio {colegio.RazonSocial} no tiene activo el curso {grado}.", CodigoError.Validacion);
        }

        e.Aplicar(s);
        e.FechaNacimiento = s.FechaNacimiento!.Value;
        e.Direccion = s.Direccion!.Trim();
        e.IdColegio = colegio.Id;
        e.Grado = grado;
        e.Jornada = s.Jornada!.Trim();
        e.Activo = EstadoRegistro.EsActivo(s.Estado);
        e.Observaciones = Texto.Opcional(s.Observaciones);
        AsignarPrincipal(e, s.AcudienteId!.Value);
    }

    /// <summary>El acudiente del formulario queda como principal; el principal anterior se desvincula.</summary>
    private static void AsignarPrincipal(Estudiante e, Guid idAcudiente)
    {
        var principal = e.Acudientes.FirstOrDefault(a => a.EsPrincipal);
        if (principal?.IdAcudiente == idAcudiente)
        {
            return;
        }
        if (principal is not null)
        {
            e.Acudientes.Remove(principal);
        }
        var existente = e.Acudientes.FirstOrDefault(a => a.IdAcudiente == idAcudiente);
        if (existente is not null)
        {
            existente.EsPrincipal = true;
        }
        else
        {
            e.Acudientes.Add(new EstudianteAcudiente { IdEstudiante = e.Id, IdAcudiente = idAcudiente, EsPrincipal = true });
        }
    }

    public override async Task<string?> MotivoNoEliminarAsync(Guid id, CancellationToken ct = default)
    {
        var rutas = await Db.RutasEstudiantes.Where(r => r.IdEstudiante == id)
            .Select(r => r.Ruta.Codigo + " · " + r.Ruta.Nombre).ToListAsync(ct);
        if (rutas.Count > 0)
        {
            return $"No se puede eliminar: está en la ruta {string.Join(", ", rutas)}. Quítalo de la ruta o cámbialo a Inactivo.";
        }
        var recorridos = await Db.ParadasViaje.CountAsync(p => p.IdEstudiante == id, ct);
        if (recorridos > 0)
        {
            return $"No se puede eliminar: aparece en {recorridos} recorrido(s). Cámbialo a Inactivo para conservar el historial.";
        }
        var facturas = await Db.Facturas.CountAsync(f => f.IdEstudiante == id, ct);
        return facturas > 0
            ? $"No se puede eliminar: tiene {facturas} factura(s) registradas. Cámbialo a Inactivo para conservar el historial."
            : null;
    }

    /// <summary>El registro de WhatsApp del acudiente se conserva sin el estudiante.</summary>
    protected override async Task AntesDeEliminarAsync(Estudiante e, CancellationToken ct)
    {
        foreach (var mensaje in await Db.MensajesWhatsapp.Where(m => m.IdEstudiante == e.Id).ToListAsync(ct))
        {
            mensaje.IdEstudiante = null;
        }
    }
}
