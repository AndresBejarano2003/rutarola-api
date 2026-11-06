using RutaRola.Application.Comun;
using RutaRola.Application.Comunicacion;

namespace RutaRola.Api.Configuracion;

/// <summary>
/// Tras una escritura exitosa (POST/PUT/PATCH/DELETE &lt; 400) de un usuario con empresa, marca la empresa
/// para que el próximo resumen de la campana genere las notificaciones de inmediato (pago recibido,
/// factura nueva…), como <c>CambiosDatos</c> en el frontend. Las operaciones sobre la propia bandeja y
/// la autenticación no cuentan.
/// </summary>
public static class CambiosDatos
{
    public static IApplicationBuilder UseCambiosDatos(this IApplicationBuilder app) => app.Use(async (http, siguiente) =>
    {
        await siguiente();
        if (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method) || HttpMethods.IsOptions(http.Request.Method)
            || http.Response.StatusCode >= 400
            || http.Request.Path.StartsWithSegments("/api/v1/notificaciones")
            || http.Request.Path.StartsWithSegments("/api/v1/auth"))
        {
            return;
        }
        if (http.RequestServices.GetRequiredService<IContextoUsuario>().IdEmpresa is { } empresa)
        {
            http.RequestServices.GetRequiredService<RevisionNotificaciones>().Marcar(empresa);
        }
    });
}
