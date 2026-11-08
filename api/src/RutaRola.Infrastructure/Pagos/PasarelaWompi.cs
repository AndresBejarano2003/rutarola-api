using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RutaRola.Application.Suscripciones;

namespace RutaRola.Infrastructure.Pagos;

/// <summary>Consulta transacciones en el API de Wompi (sandbox o producción según la llave pública).</summary>
internal sealed class PasarelaWompi(IOptions<OpcionesWompi> opciones) : IPasarelaPagos, IDisposable
{
    private readonly HttpClient http = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) })
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    public async Task<TransaccionPasarela?> ConsultarAsync(string idTransaccion, CancellationToken ct)
    {
        var o = opciones.Value;
        using var peticion = new HttpRequestMessage(HttpMethod.Get, $"{o.UrlApi}/transactions/{Uri.EscapeDataString(idTransaccion)}");
        peticion.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", o.LlavePublica);
        using var respuesta = await http.SendAsync(peticion, ct);
        if (respuesta.StatusCode == HttpStatusCode.NotFound) return null;
        respuesta.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await respuesta.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return json.RootElement.TryGetProperty("data", out var data) ? FirmasWompi.DeJson(data) : null;
    }

    public void Dispose() => http.Dispose();
}
