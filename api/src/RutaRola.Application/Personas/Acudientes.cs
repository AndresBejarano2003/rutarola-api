using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Domain.Personas;
using RutaRola.Domain.Seguridad;

namespace RutaRola.Application.Personas;

public record AcudienteDto(
    Guid Id, string TipoDocumento, string NumeroDocumento, string PrimerNombre, string SegundoNombre, string PrimerApellido,
    string SegundoApellido, string Telefono, string TelefonoAlterno, string Correo, string Direccion, string Usuario,
    string Contrasena, string Estado, DateTime FechaCreacion, DateTime? FechaActualizacion);

public record SolicitudAcudiente(
    string? TipoDocumento, string? NumeroDocumento, string? PrimerNombre, string? SegundoNombre, string? PrimerApellido,
    string? SegundoApellido, string? Telefono, string? TelefonoAlterno, string? Correo, string? Direccion, string? Usuario,
    string? Contrasena, string? Estado) : ISolicitudPersona, ISolicitudAcceso;

public record FiltrosAcudiente(string? Texto = null, string? Estado = null);

public class ValidadorSolicitudAcudiente : AbstractValidator<SolicitudAcudiente>
{
    public ValidadorSolicitudAcudiente()
    {
        this.Persona();
        this.Acceso();
        RuleFor(x => x.Telefono).NotEmpty().WithMessage("Ingresa el teléfono.").TelefonoValido();
        RuleFor(x => x.TelefonoAlterno).TelefonoValido();
        RuleFor(x => x.Correo).CorreoPersona();
        RuleFor(x => x.Direccion).MaximumLength(120).WithMessage("La dirección admite máximo 120 caracteres.");
        RuleFor(x => x.Estado).EstadoRegistroValido();
    }
}

/// <summary>
/// Acudientes. Documento único por empresa; su acceso al portal es un usuario con rol Acudiente;
/// no se elimina si tiene estudiantes a cargo o facturas.
/// </summary>
public class ServicioAcudientes(IBaseDatos db, ServicioAcceso acceso)
    : ServicioCrud<Acudiente, AcudienteDto, SolicitudAcudiente, FiltrosAcudiente>(db)
{
    protected override string Entidad => "El acudiente";
    protected override DbSet<Acudiente> Conjunto => Db.Acudientes;

    protected override IQueryable<Acudiente> Filtrar(IQueryable<Acudiente> consulta, FiltrosAcudiente f)
    {
        consulta = consulta.BuscarPersona(f.Texto);
        if (Texto.Opcional(f.Estado) is { } estado)
        {
            var activo = EstadoRegistro.EsActivo(estado);
            consulta = consulta.Where(a => a.Activo == activo);
        }
        return consulta;
    }

    protected override IOrderedQueryable<Acudiente> Ordenar(IQueryable<Acudiente> consulta) => consulta.OrdenarPorApellido();

    protected override string Etiqueta(Acudiente a) => a.NombreCompleto;

    protected override async Task<IReadOnlyList<AcudienteDto>> MapearAsync(IReadOnlyList<Acudiente> registros, bool detalle, CancellationToken ct)
    {
        var usuarios = await acceso.NombresAsync(registros.Select(a => a.IdUsuario), ct);
        return registros.Select(a => new AcudienteDto(
            a.Id, a.TipoDocumento, a.NumeroDocumento, a.PrimerNombre, a.SegundoNombre ?? "", a.PrimerApellido,
            a.SegundoApellido ?? "", a.Telefono, a.TelefonoAlterno ?? "", a.Correo ?? "", a.Direccion ?? "",
            a.IdUsuario is { } u ? usuarios.GetValueOrDefault(u, "") : "", "",
            EstadoRegistro.Texto(a.Activo), a.FechaCreacion, a.FechaActualizacion)).ToList();
    }

    protected override async Task AplicarAsync(Acudiente a, SolicitudAcudiente s, bool nuevo, CancellationToken ct)
    {
        await Db.Acudientes.ValidarDocumentoUnicoAsync(s, nuevo ? null : a.Id, "un acudiente", ct);

        a.Aplicar(s);
        a.Telefono = s.Telefono!.Trim();
        a.TelefonoAlterno = Texto.Opcional(s.TelefonoAlterno);
        a.Correo = Texto.Opcional(s.Correo)?.ToLowerInvariant();
        a.Direccion = Texto.Opcional(s.Direccion);
        a.Activo = EstadoRegistro.EsActivo(s.Estado);
        a.IdUsuario = await acceso.AplicarAsync(a.IdUsuario, s, Roles.Acudiente, a.NombreCompleto, ct);
    }

    public override async Task<string?> MotivoNoEliminarAsync(Guid id, CancellationToken ct = default)
    {
        var estudiantes = await Db.EstudiantesAcudientes.CountAsync(x => x.IdAcudiente == id, ct);
        if (estudiantes > 0)
        {
            return $"No se puede eliminar: tiene {estudiantes} estudiante(s) a cargo. Reasígnalos a otro acudiente o cámbialo a Inactivo.";
        }
        var facturas = await Db.Facturas.CountAsync(f => f.IdAcudiente == id, ct);
        return facturas > 0
            ? $"No se puede eliminar: tiene {facturas} factura(s) registradas. Cámbialo a Inactivo para conservar el historial."
            : null;
    }

    /// <summary>Su usuario y el registro de WhatsApp (solo existe si tuvo facturas, que ya se eliminaron).</summary>
    protected override async Task AntesDeEliminarAsync(Acudiente a, CancellationToken ct)
    {
        Db.MensajesWhatsapp.RemoveRange(await Db.MensajesWhatsapp.Where(m => m.IdAcudiente == a.Id).ToListAsync(ct));
        await acceso.EliminarAsync(a.IdUsuario, ct);
    }
}
