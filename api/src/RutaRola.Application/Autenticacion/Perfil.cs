using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Domain.Seguridad;

namespace RutaRola.Application.Autenticacion;

/// <summary>
/// Datos propios del usuario de la sesión (<c>GET /auth/perfil</c>). <see cref="EditaNombre"/>: el administrador y el
/// SuperAdmin cambian su nombre; el de conductores, monitoras y acudientes es el de su documento y lo corrige el
/// administrador en Personas.
/// </summary>
public record PerfilDto(string Usuario, string Rol, string Nombre, bool EditaNombre, string Correo, string Telefono, string Foto);

/// <summary><c>PUT /auth/perfil</c>: foto (data URL, "" = quitar), teléfono, correo y, si aplica, nombre.</summary>
public record SolicitudPerfil(string? Nombre, string? Correo, string? Telefono, string? Foto);

public class ValidadorSolicitudPerfil : AbstractValidator<SolicitudPerfil>
{
    public ValidadorSolicitudPerfil()
    {
        RuleFor(x => x.Nombre).MaximumLength(150).WithMessage("El nombre admite máximo 150 caracteres.");
        RuleFor(x => x.Correo).EmailAddress().WithMessage("Ingresa un correo válido.").When(x => !string.IsNullOrWhiteSpace(x.Correo));
        RuleFor(x => x.Correo).MaximumLength(150).WithMessage("El correo admite máximo 150 caracteres.");
        RuleFor(x => x.Telefono).TelefonoValido().When(x => !string.IsNullOrWhiteSpace(x.Telefono));
        RuleFor(x => x.Foto).FotoValida();
    }
}

/// <summary>
/// "Mi cuenta": cada usuario edita SUS datos de contacto y su foto (nunca los de otro: el usuario sale del token).
/// Administrador/SuperAdmin → fila de <c>usuarios</c>; conductor/monitora → su empleado; acudiente → su acudiente
/// (así el cambio se ve en Personas, en el portal y en la Ruta del día). Devuelve la sesión actualizada.
/// </summary>
public class ServicioPerfil(IBaseDatos db, IContextoUsuario contexto, ServicioAutenticacion autenticacion)
{
    public async Task<PerfilDto> ObtenerAsync(CancellationToken ct = default)
    {
        var usuario = await UsuarioAsync(ct);
        switch (usuario.Rol.Nombre)
        {
            case Roles.Conductor or Roles.Monitora:
                var e = await db.Empleados.AsNoTracking().FirstOrDefaultAsync(x => x.IdUsuario == usuario.Id, ct)
                    ?? throw ErrorNegocio.NoEncontrado("El empleado");
                return new PerfilDto(usuario.NombreUsuario, Roles.CodigoSesion(usuario.Rol.Nombre), e.NombreCompleto, false, e.Correo ?? "", e.Telefono, e.Foto ?? "");
            case Roles.Acudiente:
                var a = await db.Acudientes.AsNoTracking().FirstOrDefaultAsync(x => x.IdUsuario == usuario.Id, ct)
                    ?? throw ErrorNegocio.NoEncontrado("El acudiente");
                return new PerfilDto(usuario.NombreUsuario, Roles.CodigoSesion(usuario.Rol.Nombre), a.NombreCompleto, false, a.Correo ?? "", a.Telefono, a.Foto ?? "");
            default:
                return new PerfilDto(usuario.NombreUsuario, Roles.CodigoSesion(usuario.Rol.Nombre), usuario.Nombre, true,
                    usuario.Email ?? "", usuario.Telefono ?? "", usuario.Foto ?? "");
        }
    }

    public async Task<SesionDto> ActualizarAsync(SolicitudPerfil s, CancellationToken ct = default)
    {
        var usuario = await UsuarioAsync(ct);
        var correo = Texto.Opcional(s.Correo)?.ToLowerInvariant();
        var telefono = Texto.Opcional(s.Telefono);
        var foto = Fotos.Normalizar(s.Foto);
        switch (usuario.Rol.Nombre)
        {
            case Roles.Conductor or Roles.Monitora:
                var e = await db.Empleados.FirstOrDefaultAsync(x => x.IdUsuario == usuario.Id, ct)
                    ?? throw ErrorNegocio.NoEncontrado("El empleado");
                e.Telefono = telefono ?? throw new ErrorNegocio("Ingresa tu teléfono de contacto.", CodigoError.Validacion);
                e.Correo = correo;
                e.Foto = foto;
                break;
            case Roles.Acudiente:
                var a = await db.Acudientes.FirstOrDefaultAsync(x => x.IdUsuario == usuario.Id, ct)
                    ?? throw ErrorNegocio.NoEncontrado("El acudiente");
                a.Telefono = telefono ?? throw new ErrorNegocio("Ingresa tu teléfono de contacto.", CodigoError.Validacion);
                a.Correo = correo;
                a.Foto = foto;
                break;
            default:
                usuario.Nombre = Texto.Opcional(s.Nombre) ?? throw new ErrorNegocio("Ingresa tu nombre.", CodigoError.Validacion);
                // El correo también sirve para iniciar sesión: único frente a correos y usuarios de todo el sistema.
                if (correo is not null && await db.Usuarios.IgnoreQueryFilters([FiltrosConsulta.Empresa])
                        .AnyAsync(u => u.Id != usuario.Id && (u.Email == correo || u.NombreUsuario == correo), ct))
                {
                    throw new ErrorNegocio($"El correo \"{correo}\" ya está en uso.", CodigoError.Duplicado);
                }
                usuario.Email = correo;
                usuario.Telefono = telefono;
                usuario.Foto = foto;
                break;
        }
        await db.SaveChangesAsync(ct);
        return await autenticacion.ObtenerSesionAsync(usuario.Id, ct);
    }

    private async Task<Usuario> UsuarioAsync(CancellationToken ct)
    {
        var id = contexto.IdUsuario ?? throw new ErrorNegocio(ServicioAutenticacion.MensajeSesionInvalida, CodigoError.NoAutenticado);
        return await db.Usuarios.IgnoreQueryFilters([FiltrosConsulta.Empresa]).Include(u => u.Rol).FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new ErrorNegocio(ServicioAutenticacion.MensajeSesionInvalida, CodigoError.NoAutenticado);
    }
}
