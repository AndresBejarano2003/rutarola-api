using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Domain.Seguridad;

namespace RutaRola.Application.Empresas;

/// <summary>Usuario administrador de una empresa (lo ve el SuperAdmin). Nunca incluye la contraseña.</summary>
public record AdministradorEmpresaDto(
    Guid Id, string Nombre, string Usuario, string Email, string Telefono, string Estado, DateTime? UltimoLogin, DateTime FechaCreacion);

/// <summary><c>contrasena</c>: obligatoria al crear; al editar, vacía = se conserva.</summary>
public record SolicitudAdministradorEmpresa(string? Nombre, string? Usuario, string? Email, string? Telefono, string? Estado, string? Contrasena);

public record SolicitudContrasenaAdministrador(string? Nueva);

public class ValidadorSolicitudAdministradorEmpresa : AbstractValidator<SolicitudAdministradorEmpresa>
{
    public ValidadorSolicitudAdministradorEmpresa()
    {
        RuleFor(x => x.Nombre).NotEmpty().WithMessage("Ingresa el nombre del administrador.").MaximumLength(150);
        RuleFor(x => x.Usuario).UsuarioValido();
        RuleFor(x => x.Email).CorreoOpcional();
        RuleFor(x => x.Telefono).MaximumLength(20);
        RuleFor(x => x.Estado).EstadoRegistroValido();
        RuleFor(x => x.Contrasena).ContrasenaValida().When(x => !string.IsNullOrEmpty(x.Contrasena));
    }
}

public class ValidadorSolicitudContrasenaAdministrador : AbstractValidator<SolicitudContrasenaAdministrador>
{
    public ValidadorSolicitudContrasenaAdministrador() => RuleFor(x => x.Nueva).ContrasenaValida();
}

/// <summary>
/// Administradores de cada empresa, gestionados por la plataforma (SuperAdmin, <c>empresas.gestionar</c>):
/// listar, crear otro, editar, inactivar y restablecer la contraseña (p. ej. si el cliente la olvidó).
/// Cambiar la contraseña o inactivar cierra todas sus sesiones. Trabaja sin filtro de empresa, pero cada
/// usuario debe ser AdminEmpresa de ESA empresa (si no, 404). Queda en auditoría (tabla <c>usuarios</c>).
/// </summary>
public class ServicioAdministradoresEmpresa(IBaseDatos db, IPasswordHasher<Usuario> hasher, TimeProvider reloj)
{
    public async Task<IReadOnlyList<AdministradorEmpresaDto>> ListarAsync(Guid idEmpresa, CancellationToken ct = default)
    {
        await ValidarEmpresaAsync(idEmpresa, ct);
        return await Administradores(idEmpresa).OrderBy(u => u.Nombre).Select(Proyeccion).ToListAsync(ct);
    }

    public async Task<AdministradorEmpresaDto> CrearAsync(Guid idEmpresa, SolicitudAdministradorEmpresa s, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(s.Contrasena))
        {
            throw new ErrorNegocio("Ingresa la contraseña.", CodigoError.Validacion);
        }
        await ValidarEmpresaAsync(idEmpresa, ct);
        db.UsarModoSistema();
        var usuario = new Usuario
        {
            IdEmpresa = idEmpresa,
            IdRol = await db.Roles.Where(r => r.Nombre == Roles.AdminEmpresa).Select(r => r.Id).SingleAsync(ct),
        };
        await AplicarAsync(usuario, s, ct);
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(idEmpresa, usuario.Id, ct);
    }

    public async Task<AdministradorEmpresaDto> ActualizarAsync(Guid idEmpresa, Guid id, SolicitudAdministradorEmpresa s, CancellationToken ct = default)
    {
        db.UsarModoSistema();
        var usuario = await Administradores(idEmpresa).AsTracking().FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw ErrorNegocio.NoEncontrado("El administrador");
        var estabaActivo = usuario.Activo;
        var cambiaClave = await AplicarAsync(usuario, s, ct);
        if (cambiaClave || (estabaActivo && !usuario.Activo))
        {
            await RevocarSesionesAsync(usuario.Id, ct);
        }
        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(idEmpresa, id, ct);
    }

    /// <summary>Restablece la contraseña y cierra todas las sesiones del administrador.</summary>
    public async Task CambiarContrasenaAsync(Guid idEmpresa, Guid id, SolicitudContrasenaAdministrador s, CancellationToken ct = default)
    {
        db.UsarModoSistema();
        var usuario = await Administradores(idEmpresa).AsTracking().FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw ErrorNegocio.NoEncontrado("El administrador");
        usuario.PasswordHash = hasher.HashPassword(usuario, s.Nueva!);
        await RevocarSesionesAsync(usuario.Id, ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task<AdministradorEmpresaDto> ObtenerAsync(Guid idEmpresa, Guid id, CancellationToken ct) =>
        await Administradores(idEmpresa).Where(u => u.Id == id).Select(Proyeccion).FirstOrDefaultAsync(ct)
        ?? throw ErrorNegocio.NoEncontrado("El administrador");

    /// <summary>Copia los datos; devuelve si cambió la contraseña. El usuario es único en todo el sistema.</summary>
    private async Task<bool> AplicarAsync(Usuario usuario, SolicitudAdministradorEmpresa s, CancellationToken ct)
    {
        var nombreUsuario = Texto.Usuario(s.Usuario);
        var correo = Texto.Opcional(s.Email)?.ToLowerInvariant();
        var todos = db.Usuarios.IgnoreQueryFilters([FiltrosConsulta.Empresa]).Where(u => u.Id != usuario.Id);
        if (await todos.AnyAsync(u => u.NombreUsuario == nombreUsuario, ct))
        {
            throw new ErrorNegocio($"El usuario \"{nombreUsuario}\" ya está en uso. Elige otro.", CodigoError.Duplicado);
        }
        if (correo is not null && await todos.AnyAsync(u => u.Email == correo, ct))
        {
            throw new ErrorNegocio($"El correo {correo} ya lo usa otro usuario.", CodigoError.Duplicado);
        }
        usuario.Nombre = s.Nombre!.Trim();
        usuario.NombreUsuario = nombreUsuario;
        usuario.Email = correo;
        usuario.Telefono = Texto.Opcional(s.Telefono);
        usuario.Activo = EstadoRegistro.EsActivo(s.Estado);
        if (string.IsNullOrEmpty(s.Contrasena))
        {
            return false;
        }
        usuario.PasswordHash = hasher.HashPassword(usuario, s.Contrasena);
        return true;
    }

    private IQueryable<Usuario> Administradores(Guid idEmpresa) =>
        db.Usuarios.IgnoreQueryFilters([FiltrosConsulta.Empresa]).AsNoTracking()
            .Where(u => u.IdEmpresa == idEmpresa && u.Rol.Nombre == Roles.AdminEmpresa);

    private async Task ValidarEmpresaAsync(Guid idEmpresa, CancellationToken ct)
    {
        if (!await db.Empresas.IgnoreQueryFilters([FiltrosConsulta.Empresa]).AnyAsync(e => e.Id == idEmpresa, ct))
        {
            throw ErrorNegocio.NoEncontrado("La empresa");
        }
    }

    private async Task RevocarSesionesAsync(Guid idUsuario, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow().UtcDateTime;
        foreach (var sesion in await db.SesionesUsuario.IgnoreQueryFilters().Where(x => x.IdUsuario == idUsuario && !x.Revocado).ToListAsync(ct))
        {
            sesion.Revocado = true;
            sesion.FechaRevocacion = ahora;
        }
    }

    private static readonly System.Linq.Expressions.Expression<Func<Usuario, AdministradorEmpresaDto>> Proyeccion = u => new AdministradorEmpresaDto(
        u.Id, u.Nombre, u.NombreUsuario, u.Email ?? "", u.Telefono ?? "",
        u.Activo ? EstadoRegistro.Activo : EstadoRegistro.Inactivo, u.UltimoLogin, u.FechaCreacion);
}
