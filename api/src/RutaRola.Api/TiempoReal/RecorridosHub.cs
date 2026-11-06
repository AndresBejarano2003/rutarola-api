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
/// <item><c>recorridoActualizado</c> <c>{ recorridoId, fechaActualizacion, estado }</c> cuando cambian paradas, orden o estado (también, sin <c>Seguir</c>, al personal del recorrido y a los acudientes de sus estudiantes).</item>
/// </list>
/// El administrador puede llamar <c>SeguirEmpresa()</c> para recibir las posiciones de todos los recorridos (mapa).
/// Mismo acceso que <c>GET /recorridos/{id}/gps/ultima</c>: si no puede verlo, la invocación falla.
/// </summary>
[Authorize]
public class RecorridosHub(ServicioGps servicio, IContextoUsuario contexto, IAuthorizationService autorizacion) : Hub
{
    public static string GrupoRecorrido(Guid id) => $"recorrido:{id}";
    public static string GrupoEmpresa(Guid id) => $"empresa:{id}";
    public static string GrupoEmpleado(Guid id) => $"empleado:{id}";
    public static string GrupoAcudiente(Guid id) => $"acudiente:{id}";

    /// <summary>
    /// Cada conexión recibe los <c>recorridoActualizado</c> que le tocan sin pedirlos: el empleado los de sus
    /// recorridos (rutas del día) y el acudiente los de sus estudiantes (portal). Sale de los claims del token.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        UsarUsuarioDeLaConexion();
        if (contexto.IdEmpleado is { } empleado)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GrupoEmpleado(empleado));
        }
        if (contexto.IdAcudiente is { } acudiente)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GrupoAcudiente(acudiente));
        }
        await base.OnConnectedAsync();
    }

    public async Task Seguir(Guid recorridoId)
    {
        UsarUsuarioDeLaConexion();
        var alcance = await AlcanceGpsDe.ResolverAsync(autorizacion, Context.User!)
            ?? throw new HubException("No tienes permiso para seguir recorridos.");
        bool enCurso;
        try
        {
            enCurso = await servicio.VerificarAccesoAsync(recorridoId, alcance, Context.ConnectionAborted);
        }
        catch (ErrorNegocio e)
        {
            throw new HubException(e.Message);
        }
        if (alcance == AlcanceGps.Acudiente && !enCurso)
        {
            throw new HubException("La ubicación del vehículo solo se ve mientras el recorrido está en curso.");
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
    public async Task PosicionAsync(Guid idEmpresa, PosicionDto posicion, bool enCurso, CancellationToken ct = default)
    {
        try
        {
            IReadOnlyList<string> grupos = enCurso
                ? [RecorridosHub.GrupoRecorrido(posicion.RecorridoId), RecorridosHub.GrupoEmpresa(idEmpresa)]
                : [RecorridosHub.GrupoEmpresa(idEmpresa)];
            await hub.Clients.Groups(grupos).SendAsync("posicion", posicion, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning(e, "No se pudo avisar la posición del recorrido {Recorrido}", posicion.RecorridoId);
        }
    }

    public async Task RecorridoActualizadoAsync(Guid idEmpresa, AvisoRecorrido aviso, CancellationToken ct = default)
    {
        try
        {
            List<string> grupos = [RecorridosHub.GrupoRecorrido(aviso.RecorridoId), RecorridosHub.GrupoEmpresa(idEmpresa)];
            grupos.AddRange(aviso.Empleados.Select(RecorridosHub.GrupoEmpleado));
            grupos.AddRange(aviso.Acudientes.Select(RecorridosHub.GrupoAcudiente));
            await hub.Clients.Groups(grupos).SendAsync(
                "recorridoActualizado", new { recorridoId = aviso.RecorridoId, fechaActualizacion = aviso.FechaActualizacion, estado = aviso.Estado }, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning(e, "No se pudo avisar el cambio del recorrido {Recorrido}", aviso.RecorridoId);
        }
    }
}
