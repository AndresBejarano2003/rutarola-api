using System.Security.Claims;
using RutaRola.Api.Seguridad;
using RutaRola.Application.Comun;
using RutaRola.Application.Comunicacion;
using Microsoft.EntityFrameworkCore;

namespace RutaRola.Api.Procesos;

/// <summary>
/// Proceso del servidor que corre cada <c>Procesos:IntervaloMinutos</c> (15 por defecto) para cada empresa
/// activa: envía los recordatorios de cobro (<see cref="ServicioCobros"/>) y genera las notificaciones
/// (<see cref="GeneradorNotificaciones"/>), aunque nadie tenga la app abierta. Cada empresa corre en su
/// propio scope con el filtro de empresa normal (un contexto con solo su <c>id_empresa</c>), así que un
/// error o un dato de una no afecta a las demás. Con varias instancias del API, dejar
/// <c>Procesos:Habilitado</c> en una sola (el índice único de envíos evita duplicados si coinciden).
/// </summary>
public sealed class ProcesoComunicacion(IServiceScopeFactory scopes, IConfiguration configuracion, ILogger<ProcesoComunicacion> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!configuracion.GetValue("Procesos:Habilitado", true))
        {
            logger.LogInformation("Proceso de comunicación deshabilitado (Procesos:Habilitado = false)");
            return;
        }
        var intervalo = TimeSpan.FromMinutes(Math.Max(1, configuracion.GetValue("Procesos:IntervaloMinutos", 15)));
        // Deja terminar el arranque (migraciones de desarrollo) antes de la primera vuelta.
        await Task.Delay(TimeSpan.FromSeconds(10), ct).ContinuarSiCancela();
        using var temporizador = new PeriodicTimer(intervalo);
        do
        {
            await EjecutarAsync(ct);
        }
        while (!ct.IsCancellationRequested && await temporizador.WaitForNextTickAsync(ct).ContinuarSiCancela());
    }

    public async Task EjecutarAsync(CancellationToken ct)
    {
        List<Guid> empresas;
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IBaseDatos>();
            db.UsarModoSistema();
            empresas = await db.Empresas.AsNoTracking().Where(e => e.Activo).Select(e => e.Id).ToListAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Proceso de comunicación: no se pudieron leer las empresas");
            return;
        }

        foreach (var empresa in empresas)
        {
            try
            {
                using var scope = scopes.CreateScope();
                scope.ServiceProvider.GetRequiredService<ContextoUsuarioHttp>().Usar(Principal(empresa));
                var envios = await scope.ServiceProvider.GetRequiredService<ServicioCobros>().EnviarRecordatoriosAsync(ct);
                var notificaciones = await scope.ServiceProvider.GetRequiredService<GeneradorNotificaciones>().GenerarAsync(ct);
                if (envios + notificaciones > 0)
                {
                    logger.LogInformation("Empresa {Empresa}: {Envios} recordatorio(s) y {Notificaciones} notificación(es)", empresa, envios, notificaciones);
                    scope.ServiceProvider.GetRequiredService<RevisionNotificaciones>().Marcar(empresa);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Proceso de comunicación falló para la empresa {Empresa}", empresa);
            }
        }
    }

    /// <summary>Identidad del proceso: solo la empresa (sin usuario, rol ni permisos).</summary>
    private static ClaimsPrincipal Principal(Guid empresa) =>
        new(new ClaimsIdentity([new Claim(Reclamos.IdEmpresa, empresa.ToString())], authenticationType: "ProcesoServidor"));
}

internal static class Cancelacion
{
    /// <summary>Al apagar el servidor la espera termina sin excepción.</summary>
    public static async Task ContinuarSiCancela(this Task tarea)
    {
        try { await tarea; } catch (OperationCanceledException) { }
    }

    public static async Task<bool> ContinuarSiCancela(this ValueTask<bool> tarea)
    {
        try { return await tarea; } catch (OperationCanceledException) { return false; }
    }
}
