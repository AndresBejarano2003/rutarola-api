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

    private InfoCliente Cliente() => new(usuario.DireccionIp, Request.Headers.UserAgent.ToString());
}
