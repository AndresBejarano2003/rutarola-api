using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RutaRola.Application.Comun;

namespace RutaRola.Api.Errores;

/// <summary>
/// Convierte las excepciones en ProblemDetails con la extensión <c>mensaje</c> (la que muestra el
/// frontend) y, en validaciones, <c>errores</c> por campo. Nunca expone detalles internos.
/// </summary>
public class ManejadorErrores(IProblemDetailsService problemas, ILogger<ManejadorErrores> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception excepcion, CancellationToken ct)
    {
        var (estado, mensaje, errores) = Traducir(excepcion);
        if (estado >= 500)
        {
            logger.LogError(excepcion, "Error no controlado en {Metodo} {Ruta}", http.Request.Method, http.Request.Path);
        }

        http.Response.StatusCode = estado;
        var detalle = errores is null
            ? new ProblemDetails { Status = estado, Detail = mensaje }
            : new ValidationProblemDetails(errores) { Status = estado, Detail = mensaje };
        detalle.Extensions[DetallesProblema.Mensaje] = mensaje;

        return await problemas.TryWriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = detalle, Exception = excepcion });
    }

    private static (int Estado, string Mensaje, IDictionary<string, string[]>? Errores) Traducir(Exception excepcion) => excepcion switch
    {
        ErrorNegocio e => (Estado(e.Codigo), e.Message, null),
        ValidationException v => (
            StatusCodes.Status400BadRequest,
            v.Errors.FirstOrDefault()?.ErrorMessage ?? DetallesProblema.MensajePorEstado(400),
            v.Errors.GroupBy(f => Camel(f.PropertyName)).ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).ToArray())),
        DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "El registro cambió mientras lo editabas. Recarga e intenta de nuevo.", null),
        DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } =>
            (StatusCodes.Status409Conflict, "Ya existe un registro con esos datos.", null),
        DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation } } =>
            (StatusCodes.Status409Conflict, "El registro está en uso por otros registros o hace referencia a uno que no existe.", null),
        BadHttpRequestException b => (b.StatusCode, DetallesProblema.MensajePorEstado(b.StatusCode), null),
        _ => (StatusCodes.Status500InternalServerError, DetallesProblema.MensajePorEstado(500), null),
    };

    private static int Estado(CodigoError codigo) => codigo switch
    {
        CodigoError.NoEncontrado => StatusCodes.Status404NotFound,
        CodigoError.Duplicado or CodigoError.EnUso or CodigoError.Conflicto => StatusCodes.Status409Conflict,
        CodigoError.Validacion => StatusCodes.Status422UnprocessableEntity,
        CodigoError.NoAutenticado => StatusCodes.Status401Unauthorized,
        CodigoError.Prohibido => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status500InternalServerError,
    };

    private static string Camel(string nombre) =>
        string.IsNullOrEmpty(nombre) ? nombre : string.Join('.', nombre.Split('.').Select(p => char.ToLowerInvariant(p[0]) + p[1..]));
}
