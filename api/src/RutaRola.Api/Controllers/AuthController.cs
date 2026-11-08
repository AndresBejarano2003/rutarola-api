using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RutaRola.Api.Seguridad;
using RutaRola.Application.Autenticacion;
using RutaRola.Application.Comun;

namespace RutaRola.Api.Controllers;

/// <summary>
/// Sesión: access token JWT (15 min) + refresh token rotativo (un solo uso). El frontend renueva con
/// <c>/auth/refresh</c> cuando recibe 401 y, si falla, vuelve al login.
/// </summary>
public class AuthController(ServicioAutenticacion servicio, IContextoUsuario usuario) : ControladorApi
{
    /// <summary>Inicia sesión con el usuario (o correo) y la contraseña.</summary>
    /// <response code="200">Sesión y tokens.</response>
    /// <response code="401">Usuario o contraseña incorrectos.</response>
    /// <response code="403">Usuario, empresa o persona vinculada inactivos.</response>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(Autorizacion.PoliticaLimiteAutenticacion)]
    public Task<RespuestaLogin> Login(SolicitudLogin solicitud, CancellationToken ct) =>
        servicio.IniciarSesionAsync(solicitud, Cliente(), ct);

    /// <summary>Registro público de una empresa con su administrador: arranca con la prueba gratuita y deja la sesión iniciada.</summary>
    /// <response code="201">Sesión y tokens del administrador nuevo.</response>
    /// <response code="409">El NIT, el usuario o el correo ya están registrados.</response>
    [HttpPost("registro")]
    [AllowAnonymous]
    [EnableRateLimiting(Autorizacion.PoliticaLimiteAutenticacion)]
    [ProducesResponseType<RespuestaLogin>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Registro(
        RutaRola.Application.Empresas.SolicitudRegistro solicitud, [FromServices] RutaRola.Application.Empresas.ServicioRegistro registro, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await registro.RegistrarAsync(solicitud, Cliente(), ct));

    /// <summary>Cambia el refresh token por un par nuevo (el anterior deja de servir).</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(Autorizacion.PoliticaLimiteAutenticacion)]
    public Task<TokensDto> Refresh(SolicitudToken solicitud, CancellationToken ct) =>
        servicio.RenovarAsync(solicitud, Cliente(), ct);

    /// <summary>Cierra la sesión: revoca el refresh token enviado y la sesión del access token, si viene.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(SolicitudCerrarSesion? solicitud, CancellationToken ct)
    {
        await servicio.CerrarSesionAsync(solicitud?.RefreshToken, usuario.IdSesion, ct);
        return NoContent();
    }

    /// <summary>Usuario de la sesión actual, con sus permisos vigentes.</summary>
    [HttpGet("me")]
    public Task<SesionDto> Me(CancellationToken ct) =>
        servicio.ObtenerSesionAsync(usuario.IdUsuario!.Value, ct);

    /// <summary>Cambia la contraseña propia (exige la actual) y cierra las demás sesiones.</summary>
    /// <response code="204">Contraseña cambiada.</response>
    /// <response code="422">La contraseña actual no es correcta.</response>
    [HttpPut("contrasena")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> CambiarContrasena(SolicitudCambioContrasena solicitud, CancellationToken ct)
    {
        await servicio.CambiarContrasenaAsync(usuario.IdUsuario!.Value, usuario.IdSesion, solicitud, ct);
        return NoContent();
    }

    private InfoCliente Cliente() => new(usuario.DireccionIp, Request.Headers.UserAgent.ToString());
}
