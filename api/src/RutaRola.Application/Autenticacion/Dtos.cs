namespace RutaRola.Application.Autenticacion;

public record SolicitudLogin(string? Usuario, string? Contrasena);

public record SolicitudToken(string? RefreshToken);

/// <summary>Cambio de la propia contraseña: la actual y la nueva (mínimo 6).</summary>
public record SolicitudCambioContrasena(string? Actual, string? Nueva);

/// <summary>El refresh token es opcional: también se revoca la sesión del access token, si viene.</summary>
public record SolicitudCerrarSesion(string? RefreshToken);

public record EmpresaSesion(Guid Id, string Nombre);

/// <summary>
/// Usuario autenticado; mismos campos que <c>Sesion</c> en <c>RutaRolaApp/src/app/core/auth.ts</c>
/// (más empresa y permisos). <see cref="Rol"/>: ADMINISTRADOR | CONDUCTOR | MONITORA | ACUDIENTE | SUPER_ADMIN.
/// </summary>
public record SesionDto(
    string Usuario,
    string Nombre,
    string Correo,
    string Rol,
    Guid? EmpleadoId,
    Guid? AcudienteId,
    string Imagen,
    DateTime Inicio,
    EmpresaSesion? Empresa,
    IReadOnlyList<string> Permisos);

/// <summary>
/// <see cref="AccessToken"/>: JWT de vida corta para <c>Authorization: Bearer</c>.
/// <see cref="RefreshToken"/>: opaco y de un solo uso; se cambia en cada <c>/auth/refresh</c>.
/// </summary>
public record TokensDto(string AccessToken, DateTime AccessTokenExpira, string RefreshToken, DateTime RefreshTokenExpira);

public record RespuestaLogin(SesionDto Sesion, TokensDto Tokens);

/// <summary>Datos de la petición que se guardan con la sesión.</summary>
public record InfoCliente(string? DireccionIp, string? Dispositivo);
