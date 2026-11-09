using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Domain.Enums;
using RutaRola.Domain.Personas;
using RutaRola.Domain.Seguridad;

namespace RutaRola.Application.Personas;

/// <summary><c>contrasena</c> siempre llega vacía (solo escritura); <c>contrato.url</c> va vacía en los listados.</summary>
public record EmpleadoDto(
    Guid Id, string TipoDocumento, string NumeroDocumento, string PrimerNombre, string SegundoNombre, string PrimerApellido,
    string SegundoApellido, TipoEmpleado TipoEmpleado, string Telefono, string Correo, string Direccion, DateOnly FechaIngreso,
    decimal Sueldo, TipoContrato TipoContrato, ArchivoAdjuntoDto? Contrato, string NumeroLicencia, string CategoriaLicencia,
    DateOnly? VencimientoLicencia, string Usuario, string Contrasena, string Estado, DateTime FechaCreacion,
    DateTime? FechaActualizacion, string Foto);

public record SolicitudEmpleado(
    string? TipoDocumento, string? NumeroDocumento, string? PrimerNombre, string? SegundoNombre, string? PrimerApellido,
    string? SegundoApellido, TipoEmpleado? TipoEmpleado, string? Telefono, string? Correo, string? Direccion,
    DateOnly? FechaIngreso, decimal? Sueldo, TipoContrato? TipoContrato, ArchivoAdjuntoDto? Contrato, string? NumeroLicencia,
    string? CategoriaLicencia, DateOnly? VencimientoLicencia, string? Usuario, string? Contrasena, string? Estado,
    string? Foto = null)
    : ISolicitudPersona, ISolicitudAcceso;

/// <summary><c>texto</c> busca en nombres, apellidos y documento.</summary>
public record FiltrosEmpleado(string? Texto = null, TipoEmpleado? TipoEmpleado = null, string? Estado = null);

public class ValidadorSolicitudEmpleado : AbstractValidator<SolicitudEmpleado>
{
    public ValidadorSolicitudEmpleado()
    {
        this.Persona();
        this.Acceso();
        RuleFor(x => x.TipoEmpleado).NotNull().WithMessage("Selecciona el tipo de empleado.");
        RuleFor(x => x.Telefono).NotEmpty().WithMessage("Ingresa el teléfono.").TelefonoValido();
        RuleFor(x => x.Correo).CorreoPersona();
        RuleFor(x => x.Direccion).MaximumLength(120).WithMessage("La dirección admite máximo 120 caracteres.");
        RuleFor(x => x.FechaIngreso).FechaNoFutura("fecha de ingreso");
        RuleFor(x => x.Sueldo).NotNull().WithMessage("Ingresa el sueldo.")
            .GreaterThanOrEqualTo(1).WithMessage("El sueldo debe ser mayor que cero.");
        RuleFor(x => x.TipoContrato).NotNull().WithMessage("Selecciona el tipo de contrato.");
        RuleFor(x => x.Contrato!).ReglasAdjunto("contrato");
        RuleFor(x => x.Estado).EstadoRegistroValido();
        When(x => x.TipoEmpleado == Domain.Enums.TipoEmpleado.Conductor, () =>
        {
            RuleFor(x => x.NumeroLicencia).NotEmpty().WithMessage("Ingresa el número de licencia del conductor.")
                .Matches(@"^\d{5,15}$").WithMessage("El número de licencia debe tener de 5 a 15 dígitos.");
            RuleFor(x => x.CategoriaLicencia).NotEmpty().WithMessage("Selecciona la categoría de la licencia.")
                .Must(c => Catalogos.CategoriasLicencia.Contains(c)).WithMessage("La categoría de licencia no es válida.");
            RuleFor(x => x.VencimientoLicencia).NotNull().WithMessage("Ingresa el vencimiento de la licencia.");
        });
    }
}

/// <summary>
/// Conductores y monitoras. Documento único por empresa; su acceso a la Ruta del día es un usuario
/// con el rol de su tipo; no se elimina si tiene asignaciones (al eliminarlo se libera el usuario).
/// </summary>
public class ServicioEmpleados(IBaseDatos db, ServicioAcceso acceso, IContextoUsuario contexto)
    : ServicioCrud<Empleado, EmpleadoDto, SolicitudEmpleado, FiltrosEmpleado>(db)
{
    protected override string Entidad => "El empleado";
    protected override DbSet<Empleado> Conjunto => Db.Empleados;

    protected override IQueryable<Empleado> Filtrar(IQueryable<Empleado> consulta, FiltrosEmpleado f)
    {
        consulta = consulta.BuscarPersona(f.Texto);
        if (f.TipoEmpleado is { } tipo)
        {
            consulta = consulta.Where(e => e.TipoEmpleado == tipo);
        }
        if (Texto.Opcional(f.Estado) is { } estado)
        {
            var activo = EstadoRegistro.EsActivo(estado);
            consulta = consulta.Where(e => e.Activo == activo);
        }
        return consulta;
    }

    protected override IOrderedQueryable<Empleado> Ordenar(IQueryable<Empleado> consulta) => consulta.OrdenarPorApellido();

    protected override string Etiqueta(Empleado e) => e.NombreCompleto;

    protected override async Task<IReadOnlyList<EmpleadoDto>> MapearAsync(IReadOnlyList<Empleado> registros, bool detalle, CancellationToken ct)
    {
        var usuarios = await acceso.NombresAsync(registros.Select(e => e.IdUsuario), ct);
        var ids = registros.Select(e => e.IdArchivoContrato).OfType<Guid>().ToList();
        // En los listados no se carga el contenido de los archivos.
        var archivos = detalle
            ? await Db.Archivos.AsNoTracking().Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => Adjuntos.Dto(a, true), ct)
            : await Db.Archivos.AsNoTracking().Where(a => ids.Contains(a.Id))
                .Select(a => new { a.Id, Dto = new ArchivoAdjuntoDto(a.Nombre, a.TipoArchivo, a.Tamano, "") })
                .ToDictionaryAsync(a => a.Id, a => a.Dto, ct);

        return registros.Select(e => new EmpleadoDto(
            e.Id, e.TipoDocumento, e.NumeroDocumento, e.PrimerNombre, e.SegundoNombre ?? "", e.PrimerApellido,
            e.SegundoApellido ?? "", e.TipoEmpleado, e.Telefono, e.Correo ?? "", e.Direccion ?? "", e.FechaIngreso, e.Sueldo,
            e.TipoContrato, e.IdArchivoContrato is { } a ? archivos.GetValueOrDefault(a) : null, e.NumeroLicencia ?? "",
            e.CategoriaLicencia ?? "", e.FechaVencimientoLicencia,
            e.IdUsuario is { } u ? usuarios.GetValueOrDefault(u, "") : "", "",
            EstadoRegistro.Texto(e.Activo), e.FechaCreacion, e.FechaActualizacion, e.Foto ?? "")).ToList();
    }

    protected override async Task AplicarAsync(Empleado e, SolicitudEmpleado s, bool nuevo, CancellationToken ct)
    {
        await Db.Empleados.ValidarDocumentoUnicoAsync(s, nuevo ? null : e.Id, "un empleado", ct);

        e.Aplicar(s);
        e.TipoEmpleado = s.TipoEmpleado!.Value;
        e.Telefono = s.Telefono!.Trim();
        e.Correo = Texto.Opcional(s.Correo)?.ToLowerInvariant();
        e.Direccion = Texto.Opcional(s.Direccion);
        e.FechaIngreso = s.FechaIngreso!.Value;
        e.Sueldo = s.Sueldo!.Value;
        e.TipoContrato = s.TipoContrato!.Value;
        e.Activo = EstadoRegistro.EsActivo(s.Estado);
        var conductor = e.TipoEmpleado == TipoEmpleado.Conductor;
        e.NumeroLicencia = conductor ? Texto.Opcional(s.NumeroLicencia) : null;
        e.CategoriaLicencia = conductor ? Texto.Opcional(s.CategoriaLicencia) : null;
        e.FechaVencimientoLicencia = conductor ? s.VencimientoLicencia : null;

        e.IdArchivoContrato = await Adjuntos.AplicarAsync(Db, e.IdArchivoContrato, s.Contrato, contexto.IdUsuario, "contrato", ct);
        var rol = conductor ? Roles.Conductor : Roles.Monitora;
        e.IdUsuario = await acceso.AplicarAsync(e.IdUsuario, s, rol, e.NombreCompleto, ct);
    }

    public override async Task<string?> MotivoNoEliminarAsync(Guid id, CancellationToken ct = default)
    {
        var total = await Db.AsignacionesRuta.CountAsync(a => a.IdConductor == id || a.IdMonitora == id, ct);
        return total > 0
            ? $"No se puede eliminar: tiene {total} asignación(es) de ruta. Si dejó de trabajar, cambia su estado a Inactivo."
            : null;
    }

    protected override async Task AntesDeEliminarAsync(Empleado e, CancellationToken ct)
    {
        await acceso.EliminarAsync(e.IdUsuario, ct);
        await Adjuntos.EliminarAsync(Db, e.IdArchivoContrato, ct);
    }
}
