using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace RutaRola.Api.Seguridad;

/// <summary>
/// Permisos de un CRUD: GET exige <paramref name="leer"/>; POST, PUT y DELETE exigen
/// <paramref name="gestionar"/>. Sin el permiso → 403 (ProblemDetails).
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class PermisosCrudAttribute(string leer, string gestionar) : Attribute, IAsyncAuthorizationFilter
{
    public string Leer { get; } = leer;
    public string Gestionar { get; } = gestionar;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext contexto)
    {
        var usuario = contexto.HttpContext.User;
        if (usuario.Identity?.IsAuthenticated != true)
        {
            return; // La política por defecto ya responde 401.
        }
        var permiso = HttpMethods.IsGet(contexto.HttpContext.Request.Method) || HttpMethods.IsHead(contexto.HttpContext.Request.Method)
            ? Leer
            : Gestionar;
        var autorizacion = contexto.HttpContext.RequestServices.GetRequiredService<IAuthorizationService>();
        if (!(await autorizacion.AuthorizeAsync(usuario, permiso)).Succeeded)
        {
            contexto.Result = new ForbidResult();
        }
    }
}
