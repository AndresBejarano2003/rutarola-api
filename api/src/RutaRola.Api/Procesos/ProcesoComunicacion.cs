using System.Security.Claims;
using RutaRola.Api.Seguridad;
using RutaRola.Application.Comun;
using RutaRola.Application.Comunicacion;
using RutaRola.Application.Soporte;
using Microsoft.EntityFrameworkCore;

namespace RutaRola.Api.Procesos;

/// <summary>
/// Proceso del servidor que corre cada <c>Procesos:IntervaloMinutos</c> (15 por defecto) para cada empresa
/// activa: envía los recordatorios de cobro (<see cref="ServicioCobros"/>) y genera las notificaciones
/// (<see cref="GeneradorNotificaciones"/>), borra las lecturas GPS vencidas (<c>Gps:DiasRetencion</c>) y cierra los tickets
/// resueltos sin respuesta (<see cref="ReglasTicket.DiasCierreAutomatico"/> días), aunque nadie tenga la app abierta. Cada empresa corre en su
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

        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IBaseDatos>();
            db.UsarModoSistema();
            await DepurarGpsAsync(db, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Proceso de comunicación: falló la retención GPS");
        }

        try
        {
            using var scope = scopes.CreateScope();
            var cerrados = await scope.ServiceProvider.GetRequiredService<ServicioSoporte>().CerrarResueltosAsync(ct);
            if (cerrados > 0)
            {
                logger.LogInformation("Soporte: {Cerrados} ticket(s) resuelto(s) cerrado(s) automáticamente", cerrados);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Proceso de comunicación: falló el cierre automático de tickets");
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

    /// <summary>
    /// Retención de ubicaciones (<c>Gps:DiasRetencion</c>, 30 por defecto; 0 = conservar siempre): son
    /// posiciones de un vehículo con menores, así que las lecturas más antiguas se borran para todas las empresas.
    /// </summary>
    private async Task DepurarGpsAsync(IBaseDatos db, CancellationToken ct)
    {
        var dias = configuracion.GetValue("Gps:DiasRetencion", DiasRetencionGps);
        if (dias <= 0)
        {
            return;
        }
        var limite = DateTime.UtcNow.AddDays(-dias);
        var borradas = await db.GpsVehiculos.Where(g => g.FechaRegistro < limite).ExecuteDeleteAsync(ct);
        if (borradas > 0)
        {
            logger.LogInformation("Retención GPS: {Borradas} lectura(s) de más de {Dias} días eliminadas", borradas, dias);
        }
    }

    public const int DiasRetencionGps = 30;

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
