using System.Security.Claims;
using RutaRola.Application.Comun;

namespace RutaRola.Api.Seguridad;

/// <summary>Lee el usuario, la empresa y el rol de los claims del JWT de la petición.</summary>
public class ContextoUsuarioHttp(IHttpContextAccessor accesor) : IContextoUsuario
{
    private ClaimsPrincipal? explicito;

    private ClaimsPrincipal? Principal => explicito ?? accesor.HttpContext?.User;

    /// <summary>Fija el usuario cuando no hay petición HTTP en curso (invocaciones de SignalR).</summary>
    public void Usar(ClaimsPrincipal? usuario) => explicito = usuario;

    public bool Autenticado => Principal?.Identity?.IsAuthenticated == true;
    public Guid? IdUsuario => LeerGuid(Reclamos.IdUsuario);
    public Guid? IdEmpresa => LeerGuid(Reclamos.IdEmpresa);
    public string? Rol => Autenticado ? Principal?.FindFirstValue(Reclamos.Rol) : null;
    public Guid? IdEmpleado => LeerGuid(Reclamos.IdEmpleado);
    public Guid? IdAcudiente => LeerGuid(Reclamos.IdAcudiente);
    public Guid? IdSesion => LeerGuid(Reclamos.IdSesion);
    public string? DireccionIp => accesor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    private Guid? LeerGuid(string claim) =>
        Autenticado && Guid.TryParse(Principal?.FindFirstValue(claim), out var valor) ? valor : null;
}
