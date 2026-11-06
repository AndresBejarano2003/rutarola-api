using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using RutaRola.Tests.Infraestructura;

namespace RutaRola.Tests.Seguridad;

[Collection(ColeccionBaseDatos.Nombre)]
public class LimiteIntentosTests(BaseDatosPrueba bd)
{
    [Fact]
    public async Task Demasiados_intentos_de_login_responden_429()
    {
        await using var api = bd.Api.WithWebHostBuilder(web => web.UseSetting("Seguridad:IntentosAutenticacionPorMinuto", "3"));
        var cliente = api.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            var intento = await cliente.PostAsJsonAsync("/api/v1/auth/login", new { usuario = "admin", contrasena = "mala" }, ClienteApi.Json);
            Assert.Equal(HttpStatusCode.Unauthorized, intento.StatusCode);
        }

        var bloqueado = await cliente.PostAsJsonAsync("/api/v1/auth/login", new { usuario = "admin", contrasena = "mala" }, ClienteApi.Json);
        Assert.Equal("Demasiados intentos. Espera un minuto e intenta de nuevo.", await bloqueado.MensajeErrorAsync(HttpStatusCode.TooManyRequests));
        Assert.Equal("60", bloqueado.Headers.RetryAfter?.ToString());
    }

    [Fact]
    public async Task El_documento_OpenAPI_declara_el_esquema_Bearer()
    {
        var documento = await bd.Api.CreateClient().GetStringAsync("/openapi/v1.json");
        Assert.Contains("\"bearerFormat\": \"JWT\"", documento);
        Assert.Contains("/api/v1/auth/login", documento);
    }
}
