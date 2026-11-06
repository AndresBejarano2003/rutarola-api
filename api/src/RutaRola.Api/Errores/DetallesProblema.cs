using Microsoft.AspNetCore.Http.Features;

namespace RutaRola.Api.Errores;

public static class DetallesProblema
{
    /// <summary>Extensión de ProblemDetails que el frontend muestra tal cual (<c>error.error.mensaje</c>).</summary>
    public const string Mensaje = "mensaje";

    public static string MensajePorEstado(int estado) => estado switch
    {
        400 or 422 => "Los datos enviados no son válidos.",
        401 => "Tu sesión no es válida o expiró. Inicia sesión de nuevo.",
        403 => "No tienes permisos para realizar esta acción.",
        404 => "El recurso no existe o fue eliminado.",
        405 => "Operación no permitida.",
        409 => "Ya existe un registro con esos datos o está en uso por otros registros.",
        413 => "El archivo o la petición es demasiado grande.",
        429 => "Demasiadas solicitudes. Intenta de nuevo en un momento.",
        _ => "Ocurrió un error inesperado. Intenta de nuevo.",
    };

    /// <summary>Toda respuesta de error lleva <c>mensaje</c> (en español) y <c>traceId</c>.</summary>
    public static void Completar(ProblemDetailsContext contexto)
    {
        var problema = contexto.ProblemDetails;
        var estado = problema.Status ?? contexto.HttpContext.Response.StatusCode;
        if (!problema.Extensions.ContainsKey(Mensaje))
        {
            problema.Extensions[Mensaje] = MensajePorEstado(estado);
        }
        problema.Detail ??= (string?)problema.Extensions[Mensaje];
        problema.Extensions["traceId"] = contexto.HttpContext.Features.Get<IHttpActivityFeature>()?.Activity.TraceId.ToString()
            ?? contexto.HttpContext.TraceIdentifier;
    }
}
