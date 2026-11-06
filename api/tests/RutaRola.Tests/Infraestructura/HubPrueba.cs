using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace RutaRola.Tests.Infraestructura;

/// <summary>
/// Cliente mínimo de SignalR (protocolo JSON sobre WebSocket, sin negociación) para probar el hub
/// sin agregar el paquete del cliente.
/// </summary>
public sealed class HubPrueba : IAsyncDisposable
{
    private const char Separador = '\u001e';
    private readonly WebSocket socket;
    private readonly StringBuilder pendiente = new();
    private int invocacion;

    private HubPrueba(WebSocket socket) => this.socket = socket;

    public static async Task<HubPrueba> ConectarAsync(BaseDatosPrueba bd, string accessToken, string ruta = "/hubs/recorridos")
    {
        var cliente = bd.Api.Server.CreateWebSocketClient();
        var socket = await cliente.ConnectAsync(new Uri($"ws://localhost{ruta}?access_token={accessToken}"), CancellationToken.None);
        var hub = new HubPrueba(socket);
        await hub.EnviarAsync("""{"protocol":"json","version":1}""");
        var saludo = await hub.RecibirAsync();
        Assert.False(saludo.TryGetProperty("error", out var error), error.ToString());
        return hub;
    }

    /// <summary>Invoca un método del hub y devuelve el error (null si se completó bien).</summary>
    public async Task<string?> InvocarAsync(string metodo, params object[] argumentos)
    {
        var id = (++invocacion).ToString();
        await EnviarAsync(JsonSerializer.Serialize(new { type = 1, invocationId = id, target = metodo, arguments = argumentos }));
        while (true)
        {
            var mensaje = await RecibirAsync();
            if (mensaje.GetProperty("type").GetInt32() == 3 && mensaje.GetProperty("invocationId").GetString() == id)
            {
                return mensaje.TryGetProperty("error", out var error) ? error.GetString() : null;
            }
        }
    }

    /// <summary>Espera el siguiente mensaje del servidor con ese <paramref name="evento"/> y devuelve su primer argumento.</summary>
    public async Task<JsonElement> EsperarAsync(string evento, int segundos = 10)
    {
        using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(segundos));
        while (true)
        {
            var mensaje = await RecibirAsync(limite.Token);
            if (mensaje.GetProperty("type").GetInt32() == 1 && mensaje.GetProperty("target").GetString() == evento)
            {
                return mensaje.GetProperty("arguments")[0];
            }
        }
    }

    private Task EnviarAsync(string json) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(json + Separador), WebSocketMessageType.Text, true, CancellationToken.None);

    private async Task<JsonElement> RecibirAsync(CancellationToken ct = default)
    {
        var bufer = new byte[8192];
        while (true)
        {
            var texto = pendiente.ToString();
            var fin = texto.IndexOf(Separador);
            if (fin >= 0)
            {
                pendiente.Remove(0, fin + 1);
                var mensaje = JsonDocument.Parse(texto[..fin]).RootElement.Clone();
                if (mensaje.TryGetProperty("type", out var tipo) && tipo.GetInt32() == 6)
                {
                    continue; // ping
                }
                return mensaje;
            }
            var leido = await socket.ReceiveAsync(bufer, ct);
            if (leido.MessageType == WebSocketMessageType.Close)
            {
                throw new InvalidOperationException($"El hub cerró la conexión: {leido.CloseStatusDescription}");
            }
            pendiente.Append(Encoding.UTF8.GetString(bufer, 0, leido.Count));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (socket.State == WebSocketState.Open)
        {
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "fin", CancellationToken.None);
        }
        socket.Dispose();
    }
}
