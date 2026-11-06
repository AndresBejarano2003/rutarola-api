using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Domain.Comun;
using RutaRola.Domain.Seguridad;

namespace RutaRola.Application.Personas;

/// <summary>Identificación común (<c>DatosPersona</c> del frontend).</summary>
public interface ISolicitudPersona
{
    string? TipoDocumento { get; }
    string? NumeroDocumento { get; }
    string? PrimerNombre { get; }
    string? SegundoNombre { get; }
    string? PrimerApellido { get; }
    string? SegundoApellido { get; }
}

/// <summary>Credenciales de acceso (<c>DatosAcceso</c>): la contraseña es de solo escritura.</summary>
public interface ISolicitudAcceso
{
    string? Usuario { get; }
    string? Contrasena { get; }
}

/// <summary>Reglas de <c>controlesPersona</c> y <c>controlesAcceso</c> del frontend.</summary>
public static class ReglasPersona
{
    public const string PatronDocumento = "^[A-Za-z0-9]{5,15}$";

    public static void Persona<T>(this AbstractValidator<T> v) where T : ISolicitudPersona
    {
        v.RuleFor(x => x.TipoDocumento).NotEmpty().WithMessage("Selecciona el tipo de documento.")
            .Must(t => Catalogos.TiposDocumento.Contains(t)).WithMessage("El tipo de documento no es válido.");
        v.RuleFor(x => x.NumeroDocumento).NotEmpty().WithMessage("Ingresa el número de documento.")
            .Must(n => n is null || System.Text.RegularExpressions.Regex.IsMatch(n.Trim(), PatronDocumento))
            .WithMessage("El número de documento debe tener de 5 a 15 letras o números.");
        v.RuleFor(x => x.PrimerNombre).NotEmpty().WithMessage("Ingresa el primer nombre.")
            .MaximumLength(50).WithMessage("El nombre admite máximo 50 caracteres.");
        v.RuleFor(x => x.SegundoNombre).MaximumLength(50).WithMessage("El nombre admite máximo 50 caracteres.");
        v.RuleFor(x => x.PrimerApellido).NotEmpty().WithMessage("Ingresa el primer apellido.")
            .MaximumLength(50).WithMessage("El apellido admite máximo 50 caracteres.");
        v.RuleFor(x => x.SegundoApellido).MaximumLength(50).WithMessage("El apellido admite máximo 50 caracteres.");
    }

    /// <summary>La contraseña es opcional aquí (vacía al editar = conservar); al crear la exige <see cref="ServicioAcceso"/>.</summary>
    public static void Acceso<T>(this AbstractValidator<T> v) where T : ISolicitudAcceso
    {
        v.RuleFor(x => x.Usuario).UsuarioValido();
        v.RuleFor(x => x.Contrasena)
            .Length(ReglasComunes.ContrasenaMinima, ReglasComunes.ContrasenaMaxima)
            .WithMessage($"La contraseña debe tener al menos {ReglasComunes.ContrasenaMinima} caracteres.")
            .When(x => !string.IsNullOrEmpty(x.Contrasena));
    }

    public static void Aplicar(this Persona p, ISolicitudPersona s)
    {
        p.TipoDocumento = s.TipoDocumento!.Trim();
        p.NumeroDocumento = s.NumeroDocumento!.Trim();
        p.PrimerNombre = s.PrimerNombre!.Trim();
        p.SegundoNombre = Texto.Opcional(s.SegundoNombre);
        p.PrimerApellido = s.PrimerApellido!.Trim();
        p.SegundoApellido = Texto.Opcional(s.SegundoApellido);
    }

    /// <summary>Busca en nombre completo y número de documento, sin tildes ni mayúsculas (<c>coincideTexto</c>).</summary>
    public static IQueryable<T> BuscarPersona<T>(this IQueryable<T> consulta, string? texto) where T : Persona
    {
        if (Texto.Opcional(texto) is not { } valor)
        {
            return consulta;
        }
        var busqueda = Busqueda.Normalizar(valor);
        return consulta.Where(p => FuncionesBd.SinTildes(
            p.PrimerNombre
            + (p.SegundoNombre != null ? " " + p.SegundoNombre : "")
            + " " + p.PrimerApellido
            + (p.SegundoApellido != null ? " " + p.SegundoApellido : "")
            + " " + p.NumeroDocumento).Contains(busqueda));
    }

    /// <summary>Orden alfabético por primer apellido y primer nombre (<c>compararPorApellido</c>).</summary>
    public static IOrderedQueryable<T> OrdenarPorApellido<T>(this IQueryable<T> consulta) where T : Persona =>
        consulta.OrderBy(p => FuncionesBd.SinTildes(p.PrimerApellido)).ThenBy(p => FuncionesBd.SinTildes(p.PrimerNombre)).ThenBy(p => p.Id);

    /// <summary>"Ya existe un empleado con el documento CC 123." (409).</summary>
    public static async Task ValidarDocumentoUnicoAsync<T>(
        this IQueryable<T> registros, ISolicitudPersona s, Guid? idActual, string entidad, CancellationToken ct) where T : Persona
    {
        var tipo = s.TipoDocumento!.Trim();
        var numero = s.NumeroDocumento!.Trim();
        if (await registros.AnyAsync(r => r.Id != idActual && r.TipoDocumento == tipo && r.NumeroDocumento == numero, ct))
        {
            throw new ErrorNegocio($"Ya existe {entidad} con el documento {tipo} {numero}.", CodigoError.Duplicado);
        }
    }

    public static string Iniciales(Persona p) =>
        $"{p.PrimerNombre.FirstOrDefault()}{p.PrimerApellido.FirstOrDefault()}".ToUpperInvariant();
}

/// <summary>
/// Usuario de acceso de un empleado o acudiente (tabla <c>usuarios</c>). El usuario es único en TODO
/// el sistema (también frente a correos de ingreso); la contraseña se guarda con hash y nunca se
/// devuelve. Cambiar la contraseña cierra las sesiones abiertas de ese usuario.
/// </summary>
public class ServicioAcceso(IBaseDatos db, IPasswordHasher<Usuario> hasher, IContextoUsuario contexto, TimeProvider reloj)
{
    public async Task<IReadOnlyDictionary<Guid, string>> NombresAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var lista = ids.OfType<Guid>().Distinct().ToList();
        return lista.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Usuarios.Where(u => lista.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.NombreUsuario, ct);
    }

    /// <summary>Crea o actualiza el usuario y devuelve su id (asignarlo a <c>IdUsuario</c> del titular).</summary>
    public async Task<Guid> AplicarAsync(Guid? idUsuario, ISolicitudAcceso solicitud, string rol, string nombre, CancellationToken ct)
    {
        var nombreUsuario = Texto.Usuario(solicitud.Usuario);
        var actual = idUsuario is { } id ? await db.Usuarios.FirstOrDefaultAsync(u => u.Id == id, ct) : null;
        var idActual = actual?.Id ?? Guid.Empty;
        // El usuario es único en TODO el sistema: se busca también en las otras empresas.
        if (await db.Usuarios.IgnoreQueryFilters([FiltrosConsulta.Empresa])
                .AnyAsync(u => u.Id != idActual && (u.NombreUsuario == nombreUsuario || u.Email == nombreUsuario), ct))
        {
            throw new ErrorNegocio($"El usuario \"{solicitud.Usuario!.Trim()}\" ya está en uso. Elige otro.", CodigoError.Duplicado);
        }

        var contrasena = string.IsNullOrEmpty(solicitud.Contrasena) ? null : solicitud.Contrasena;
        if (actual is null)
        {
            if (contrasena is null)
            {
                throw new ErrorNegocio(
                    $"La contraseña debe tener al menos {ReglasComunes.ContrasenaMinima} caracteres.", CodigoError.Validacion);
            }
            actual = new Usuario
            {
                IdEmpresa = contexto.IdEmpresa
                    ?? throw new ErrorNegocio("Se requiere una empresa para crear este registro.", CodigoError.Prohibido),
            };
            db.Usuarios.Add(actual);
        }

        actual.NombreUsuario = nombreUsuario;
        actual.Nombre = nombre;
        actual.IdRol = await db.Roles.Where(r => r.Nombre == rol).Select(r => r.Id).SingleAsync(ct);
        if (contrasena is not null)
        {
            var habiaClave = actual.PasswordHash.Length > 0;
            actual.PasswordHash = hasher.HashPassword(actual, contrasena);
            if (habiaClave)
            {
                await RevocarSesionesAsync(actual.Id, ct);
            }
        }
        return actual.Id;
    }

    /// <summary>Al eliminar al titular se libera el usuario (sus sesiones se borran en cascada).</summary>
    public async Task EliminarAsync(Guid? idUsuario, CancellationToken ct)
    {
        if (idUsuario is { } id && await db.Usuarios.FirstOrDefaultAsync(u => u.Id == id, ct) is { } usuario)
        {
            db.Usuarios.Remove(usuario);
        }
    }

    private async Task RevocarSesionesAsync(Guid idUsuario, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow().UtcDateTime;
        foreach (var sesion in await db.SesionesUsuario.Where(s => s.IdUsuario == idUsuario && !s.Revocado).ToListAsync(ct))
        {
            sesion.Revocado = true;
            sesion.FechaRevocacion = ahora;
        }
    }
}
