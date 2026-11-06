using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using RutaRola.Api.Seguridad;
using RutaRola.Application.Comun;
using RutaRola.Application.Operacion;
using RutaRola.Domain.Seguridad;

namespace RutaRola.Api.TiempoReal;

/// <summary>
/// Tiempo real de los recorridos en <c>/hubs/recorridos</c> (token en <c>?access_token=</c>, como lo envía
/// <c>@microsoft/signalr</c>). El cliente llama <c>Seguir(recorridoId)</c> y recibe:
/// <list type="bullet">
/// <item><c>posicion</c> (<see cref="PosicionDto"/>) cada vez que llega un lote GPS;</item>
/// <item><c>recorridoActualizado</c> <c>{ recorridoId, fechaActualizacion }</c> cuando cambian paradas, orden o estado.</item>
/// </list>
/// El administrador puede llamar <c>SeguirEmpresa()</c> para recibir las posiciones de todos los recorridos (mapa).
/// Mismo acceso que <c>GET /recorridos/{id}/gps/ultima</c>: si no puede verlo, la invocación falla.
/// </summary>
[Authorize]
public class RecorridosHub(ServicioGps servicio, IContextoUsuario contexto, IAuthorizationService autorizacion) : Hub
{
    public static string GrupoRecorrido(Guid id) => $"recorrido:{id}";
    public static string GrupoEmpresa(Guid id) => $"empresa:{id}";

    public async Task Seguir(Guid recorridoId)
    {
        UsarUsuarioDeLaConexion();
        var alcance = await AlcanceGpsDe.ResolverAsync(autorizacion, Context.User!)
            ?? throw new HubException("No tienes permiso para seguir recorridos.");
        try
        {
            await servicio.VerificarAccesoAsync(recorridoId, alcance, Context.ConnectionAborted);
        }
        catch (ErrorNegocio e)
        {
            throw new HubException(e.Message);
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, GrupoRecorrido(recorridoId));
    }

    public Task Dejar(Guid recorridoId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, GrupoRecorrido(recorridoId));

    public async Task SeguirEmpresa()
    {
        UsarUsuarioDeLaConexion();
        if (!(await autorizacion.AuthorizeAsync(Context.User!, Permisos.RecorridosLeer)).Succeeded || contexto.IdEmpresa is not { } empresa)
        {
            throw new HubException("No tienes permiso para ver todos los recorridos.");
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, GrupoEmpresa(empresa));
    }

    /// <summary>En los transportes que no son WebSocket no hay HttpContext de la invocación: se usa el de la conexión.</summary>
    private void UsarUsuarioDeLaConexion()
    {
        if (contexto is ContextoUsuarioHttp http)
        {
            http.Usar(Context.User);
        }
    }
}

/// <summary>Envía los avisos por SignalR; un fallo se registra y no afecta la petición que lo originó.</summary>
public class AvisosRecorridoSignalR(IHubContext<RecorridosHub> hub, ILogger<AvisosRecorridoSignalR> log) : IAvisosRecorrido
{
    public async Task PosicionAsync(Guid idEmpresa, PosicionDto posicion, CancellationToken ct = default)
    {
        try
        {
            await hub.Clients.Groups(RecorridosHub.GrupoRecorrido(posicion.RecorridoId), RecorridosHub.GrupoEmpresa(idEmpresa))
                .SendAsync("posicion", posicion, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning(e, "No se pudo avisar la posición del recorrido {Recorrido}", posicion.RecorridoId);
        }
    }

    public async Task RecorridoActualizadoAsync(Guid idEmpresa, Guid idRecorrido, DateTime? fechaActualizacion, CancellationToken ct = default)
    {
        try
        {
            await hub.Clients.Group(RecorridosHub.GrupoRecorrido(idRecorrido))
                .SendAsync("recorridoActualizado", new { recorridoId = idRecorrido, fechaActualizacion }, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning(e, "No se pudo avisar el cambio del recorrido {Recorrido}", idRecorrido);
        }
    }
}
