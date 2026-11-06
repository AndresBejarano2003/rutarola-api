using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RutaRola.Application.Comun;
using RutaRola.Domain.Seguridad;

namespace RutaRola.Application.Autenticacion;

/// <summary>
/// Login, renovación y cierre de sesión. Reglas:
/// - se ingresa con el usuario (único en el sistema) o con el correo; el error no revela cuál falló;
/// - usuario, empresa, empleado o acudiente inactivos no entran;
/// - access token JWT de vida corta + refresh token rotativo guardado SOLO como hash;
/// - reusar un refresh ya rotado (fuera de la gracia) se trata como robo: se revocan todas las sesiones del usuario.
/// </summary>
public class ServicioAutenticacion(
    IBaseDatos db,
    IPasswordHasher<Usuario> hasher,
    IGeneradorTokens tokens,
    IOptions<OpcionesJwt> opciones,
    TimeProvider reloj,
    ILogger<ServicioAutenticacion> logger)
{
    public const string MensajeCredencialesInvalidas = "Usuario o contraseña incorrectos.";
    public const string MensajeSesionInvalida = "Tu sesión expiró. Inicia sesión de nuevo.";
    public const string MensajeInactivo = "Tu usuario está inactivo. Comunícate con el administrador.";

    private static readonly Lazy<string> HashFicticio = new(() => new PasswordHasher<Usuario>().HashPassword(new Usuario(), Guid.NewGuid().ToString()));

    private readonly OpcionesJwt jwt = opciones.Value;
    private DateTime Ahora => reloj.GetUtcNow().UtcDateTime;

    public async Task<RespuestaLogin> IniciarSesionAsync(SolicitudLogin solicitud, InfoCliente cliente, CancellationToken ct = default)
    {
        var usuarioOCorreo = Texto.Usuario(solicitud.Usuario);
        var usuario = await UsuariosSinFiltro()
            .FirstOrDefaultAsync(u => u.NombreUsuario == usuarioOCorreo || u.Email == usuarioOCorreo, ct);

        if (usuario is null)
        {
            // Mismo costo que una verificación real: no se revela si el usuario existe.
            hasher.VerifyHashedPassword(new Usuario(), HashFicticio.Value, solicitud.Contrasena ?? "");
            logger.LogInformation("Login rechazado: usuario inexistente");
            throw new ErrorNegocio(MensajeCredencialesInvalidas, CodigoError.NoAutenticado);
        }

        var resultado = hasher.VerifyHashedPassword(usuario, usuario.PasswordHash, solicitud.Contrasena ?? "");
        if (resultado == PasswordVerificationResult.Failed)
        {
            logger.LogInformation("Login rechazado: contraseña incorrecta para {IdUsuario}", usuario.Id);
            throw new ErrorNegocio(MensajeCredencialesInvalidas, CodigoError.NoAutenticado);
        }
        if (resultado == PasswordVerificationResult.SuccessRehashNeeded)
        {
            usuario.PasswordHash = hasher.HashPassword(usuario, solicitud.Contrasena!);
        }

        var titular = await ValidarAccesoAsync(usuario, ct);
        usuario.UltimoLogin = Ahora;
        var (sesionBd, tokensDto) = await CrearSesionAsync(usuario, titular, cliente, ct);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Inicio de sesión de {IdUsuario} ({Rol}), sesión {IdSesion}", usuario.Id, usuario.Rol.Nombre, sesionBd.Id);
        return new RespuestaLogin(ArmarSesion(usuario, titular, Ahora, tokensDto.Permisos), tokensDto.Tokens);
    }

    public async Task<TokensDto> RenovarAsync(SolicitudToken solicitud, InfoCliente cliente, CancellationToken ct = default)
    {
        var hash = tokens.Hash(solicitud.RefreshToken ?? "");
        var sesion = await db.SesionesUsuario
            .IgnoreQueryFilters([FiltrosConsulta.Empresa])
            .Include(s => s.Usuario).ThenInclude(u => u.Rol)
            .Include(s => s.Usuario).ThenInclude(u => u.Empresa)
            .FirstOrDefaultAsync(s => s.TokenHash == hash, ct)
            ?? throw new ErrorNegocio(MensajeSesionInvalida, CodigoError.NoAutenticado);

        if (sesion.Revocado)
        {
            var enGracia = sesion.IdReemplazadaPor is not null
                && sesion.FechaRevocacion is { } revocada
                && (Ahora - revocada).TotalSeconds <= jwt.SegundosGraciaRotacion;
            if (sesion.IdReemplazadaPor is not null && !enGracia)
            {
                logger.LogWarning("Reuso de un refresh token ya rotado (sesión {IdSesion}): se revocan las sesiones de {IdUsuario}",
                    sesion.Id, sesion.IdUsuario);
                await RevocarTodasAsync(sesion.IdUsuario, ct);
            }
            throw new ErrorNegocio(MensajeSesionInvalida, CodigoError.NoAutenticado);
        }
        if (sesion.FechaExpiracion <= Ahora)
        {
            throw new ErrorNegocio(MensajeSesionInvalida, CodigoError.NoAutenticado);
        }

        var titular = await ValidarAccesoAsync(sesion.Usuario, ct);
        var (nueva, resultado) = await CrearSesionAsync(sesion.Usuario, titular, cliente, ct);
        sesion.Revocado = true;
        sesion.FechaRevocacion = Ahora;
        sesion.IdReemplazadaPor = nueva.Id;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Otra petición rotó el mismo token al mismo tiempo: solo una gana.
            throw new ErrorNegocio(MensajeSesionInvalida, CodigoError.NoAutenticado);
        }
        return resultado.Tokens;
    }

    /// <summary>Revoca la sesión del refresh token y/o la del access token actual. Idempotente.</summary>
    public async Task CerrarSesionAsync(string? refreshToken, Guid? idSesionActual, CancellationToken ct = default)
    {
        var hash = string.IsNullOrWhiteSpace(refreshToken) ? null : tokens.Hash(refreshToken);
        var sesiones = await db.SesionesUsuario
            .IgnoreQueryFilters([FiltrosConsulta.Empresa])
            .Where(s => !s.Revocado && (s.TokenHash == hash || s.Id == idSesionActual))
            .ToListAsync(ct);
        foreach (var sesion in sesiones)
        {
            sesion.Revocado = true;
            sesion.FechaRevocacion = Ahora;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<SesionDto> ObtenerSesionAsync(Guid idUsuario, CancellationToken ct = default)
    {
        var usuario = await UsuariosSinFiltro().FirstOrDefaultAsync(u => u.Id == idUsuario, ct)
            ?? throw new ErrorNegocio(MensajeSesionInvalida, CodigoError.NoAutenticado);
        var titular = await ValidarAccesoAsync(usuario, ct);
        return ArmarSesion(usuario, titular, usuario.UltimoLogin ?? Ahora, await PermisosDeAsync(usuario.IdRol, ct));
    }

    /// <summary>
    /// Cambia la contraseña del usuario de la sesión: exige la actual, y revoca las demás sesiones (la
    /// actual sigue abierta). La auditoría registra solo "(cambiada)".
    /// </summary>
    public async Task CambiarContrasenaAsync(Guid idUsuario, Guid? idSesion, SolicitudCambioContrasena solicitud, CancellationToken ct = default)
    {
        var usuario = await UsuariosSinFiltro().FirstOrDefaultAsync(u => u.Id == idUsuario, ct)
            ?? throw new ErrorNegocio(MensajeSesionInvalida, CodigoError.NoAutenticado);
        if (hasher.VerifyHashedPassword(usuario, usuario.PasswordHash, solicitud.Actual ?? "") == PasswordVerificationResult.Failed)
        {
            throw new ErrorNegocio("La contraseña actual no es correcta.", CodigoError.Validacion);
        }
        if (solicitud.Actual == solicitud.Nueva)
        {
            throw new ErrorNegocio("La nueva contraseña debe ser distinta de la actual.", CodigoError.Validacion);
        }
        usuario.PasswordHash = hasher.HashPassword(usuario, solicitud.Nueva!);
        var otras = await db.SesionesUsuario.IgnoreQueryFilters([FiltrosConsulta.Empresa])
            .Where(s => s.IdUsuario == idUsuario && !s.Revocado && s.Id != idSesion).ToListAsync(ct);
        foreach (var s in otras)
        {
            s.Revocado = true;
            s.FechaRevocacion = Ahora;
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Cambio de contraseña de {IdUsuario}; {Revocadas} sesiones revocadas", idUsuario, otras.Count);
    }

    // ---------------------------------------------------------------------------------------------

    private IQueryable<Usuario> UsuariosSinFiltro() =>
        db.Usuarios.IgnoreQueryFilters([FiltrosConsulta.Empresa]).Include(u => u.Rol).Include(u => u.Empresa);

    /// <summary>Persona vinculada al usuario (conductor/monitora o acudiente).</summary>
    private record Titular(Guid? IdEmpleado, Guid? IdAcudiente, string? Nombre, string? Correo);

    private async Task<Titular> ValidarAccesoAsync(Usuario usuario, CancellationToken ct)
    {
        if (!usuario.Activo)
        {
            throw new ErrorNegocio(MensajeInactivo, CodigoError.Prohibido);
        }
        if (usuario.Rol.Nombre != Roles.SuperAdmin && usuario.Empresa is not { Activo: true })
        {
            throw new ErrorNegocio("La empresa de transporte está inactiva. Comunícate con RutaRola.", CodigoError.Prohibido);
        }

        switch (usuario.Rol.Nombre)
        {
            case Roles.Conductor or Roles.Monitora:
                var empleado = await db.Empleados.IgnoreQueryFilters([FiltrosConsulta.Empresa])
                    .Where(e => e.IdUsuario == usuario.Id)
                    .Select(e => new { e.Id, e.Activo, e.PrimerNombre, e.SegundoNombre, e.PrimerApellido, e.SegundoApellido, e.Correo })
                    .FirstOrDefaultAsync(ct)
                    ?? throw new ErrorNegocio("Tu usuario no está vinculado a un conductor o monitora.", CodigoError.Prohibido);
                if (!empleado.Activo)
                {
                    throw new ErrorNegocio(MensajeInactivo, CodigoError.Prohibido);
                }
                return new Titular(empleado.Id, null, Nombre(empleado.PrimerNombre, empleado.SegundoNombre, empleado.PrimerApellido, empleado.SegundoApellido), empleado.Correo);

            case Roles.Acudiente:
                var acudiente = await db.Acudientes.IgnoreQueryFilters([FiltrosConsulta.Empresa])
                    .Where(a => a.IdUsuario == usuario.Id)
                    .Select(a => new { a.Id, a.Activo, a.PrimerNombre, a.SegundoNombre, a.PrimerApellido, a.SegundoApellido, a.Correo })
                    .FirstOrDefaultAsync(ct)
                    ?? throw new ErrorNegocio("Tu usuario no está vinculado a un acudiente.", CodigoError.Prohibido);
                if (!acudiente.Activo)
                {
                    throw new ErrorNegocio(MensajeInactivo, CodigoError.Prohibido);
                }
                return new Titular(null, acudiente.Id, Nombre(acudiente.PrimerNombre, acudiente.SegundoNombre, acudiente.PrimerApellido, acudiente.SegundoApellido), acudiente.Correo);

            default:
                return new Titular(null, null, null, null);
        }
    }

    private async Task<(SesionUsuario Sesion, (TokensDto Tokens, IReadOnlyList<string> Permisos))> CrearSesionAsync(
        Usuario usuario, Titular titular, InfoCliente cliente, CancellationToken ct)
    {
        var permisos = await PermisosDeAsync(usuario.IdRol, ct);
        var refresh = tokens.CrearRefresh();
        var sesion = new SesionUsuario
        {
            IdUsuario = usuario.Id,
            TokenHash = tokens.Hash(refresh),
            DireccionIp = cliente.DireccionIp,
            Dispositivo = cliente.Dispositivo is { Length: > 300 } d ? d[..300] : cliente.Dispositivo,
            FechaCreacion = Ahora,
            FechaExpiracion = Ahora.AddDays(jwt.DiasRefresh),
        };
        db.SesionesUsuario.Add(sesion);

        var acceso = tokens.CrearAcceso(new DatosToken(
            usuario.Id, titular.Nombre ?? usuario.Nombre, usuario.IdEmpresa, usuario.Rol.Nombre,
            titular.IdEmpleado, titular.IdAcudiente, sesion.Id, permisos));
        return (sesion, (new TokensDto(acceso.Token, acceso.Expira, refresh, sesion.FechaExpiracion), permisos));
    }

    private Task<List<string>> PermisosDeAsync(Guid idRol, CancellationToken ct) =>
        db.RolesPermisos.Where(rp => rp.IdRol == idRol).Select(rp => rp.Permiso.Nombre).OrderBy(n => n).ToListAsync(ct);

    private async Task RevocarTodasAsync(Guid idUsuario, CancellationToken ct)
    {
        var activas = await db.SesionesUsuario.IgnoreQueryFilters([FiltrosConsulta.Empresa])
            .Where(s => s.IdUsuario == idUsuario && !s.Revocado).ToListAsync(ct);
        foreach (var s in activas)
        {
            s.Revocado = true;
            s.FechaRevocacion = Ahora;
        }
        await db.SaveChangesAsync(ct);
    }

    private static SesionDto ArmarSesion(Usuario usuario, Titular titular, DateTime inicio, IReadOnlyList<string> permisos) => new(
        usuario.NombreUsuario,
        titular.Nombre ?? usuario.Nombre,
        usuario.Email ?? titular.Correo ?? "",
        Roles.CodigoSesion(usuario.Rol.Nombre),
        titular.IdEmpleado,
        titular.IdAcudiente,
        "",
        inicio,
        usuario.Empresa is { } e ? new EmpresaSesion(e.Id, e.Nombre) : null,
        permisos);

    private static string Nombre(params string?[] partes) =>
        string.Join(' ', partes.Where(p => !string.IsNullOrWhiteSpace(p)));
}
