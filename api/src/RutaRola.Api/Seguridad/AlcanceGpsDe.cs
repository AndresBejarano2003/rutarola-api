using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using RutaRola.Application.Operacion;
using RutaRola.Domain.Seguridad;

namespace RutaRola.Api.Seguridad;

/// <summary>Administrador → toda la empresa; conductor/monitora → sus recorridos; acudiente → los de sus estudiantes.</summary>
public static class AlcanceGpsDe
{
    public static async Task<AlcanceGps?> ResolverAsync(IAuthorizationService autorizacion, ClaimsPrincipal usuario)
    {
        if ((await autorizacion.AuthorizeAsync(usuario, Permisos.RecorridosLeer)).Succeeded) return AlcanceGps.Empresa;
        if ((await autorizacion.AuthorizeAsync(usuario, Permisos.GpsReportar)).Succeeded) return AlcanceGps.Empleado;
        if ((await autorizacion.AuthorizeAsync(usuario, Permisos.GpsConsultar)).Succeeded) return AlcanceGps.Acudiente;
        return null;
    }
}
