using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RutaRola.Api.Configuracion;
using RutaRola.Domain.Enums;
using RutaRola.Infrastructure.Persistencia;
using RutaRola.Tests.Infraestructura;
using Microsoft.Extensions.Configuration;

namespace RutaRola.Tests.Base;

[Collection(ColeccionBaseDatos.Nombre)]
public class ApiBaseTests(BaseDatosPrueba bd)
{
    [Fact]
    public async Task Health_valida_la_conexion_a_la_base_de_datos()
    {
        var respuesta = await bd.Api.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Healthy", cuerpo.GetProperty("estado").GetString());
        Assert.Equal("Healthy", cuerpo.GetProperty("componentes").GetProperty("base_datos").GetString());
    }

    [Fact]
    public async Task Los_errores_son_ProblemDetails_con_mensaje_en_espanol()
    {
        var respuesta = await bd.Api.CreateClient().GetAsync("/api/v1/no-existe");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(404, cuerpo.GetProperty("status").GetInt32());
        Assert.Equal("El recurso no existe o fue eliminado.", cuerpo.GetProperty("mensaje").GetString());
        Assert.True(cuerpo.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task La_documentacion_OpenAPI_esta_disponible_fuera_de_produccion()
    {
        var respuesta = await bd.Api.CreateClient().GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
    }

    [Fact]
    public async Task CORS_permite_el_frontend_y_rechaza_otros_origenes()
    {
        var cliente = bd.Api.CreateClient();

        async Task<string?> Preflight(string origen)
        {
            using var peticion = new HttpRequestMessage(HttpMethod.Options, "/health");
            peticion.Headers.Add("Origin", origen);
            peticion.Headers.Add("Access-Control-Request-Method", "GET");
            var respuesta = await cliente.SendAsync(peticion);
            return respuesta.Headers.TryGetValues("Access-Control-Allow-Origin", out var v) ? v.Single() : null;
        }

        Assert.Equal("https://rutarola.netlify.app", await Preflight("https://rutarola.netlify.app"));
        Assert.Equal("http://localhost:4200", await Preflight("http://localhost:4200"));
        Assert.Null(await Preflight("https://sitio-malicioso.example"));
    }
}

public class ConvencionesTests
{
    [Theory]
    [InlineData(EstadoViaje.EnCurso, "EN_CURSO")]
    [InlineData(EstadoParada.ABordo, "A_BORDO")]
    [InlineData(EstadoPlantilla.EnUso, "EN_USO")]
    [InlineData(TipoContrato.PrestacionServicios, "PRESTACION_SERVICIOS")]
    [InlineData(TipoDocumentoVehiculo.Soat, "SOAT")]
    public void Los_enums_se_guardan_y_serializan_en_UPPER_SNAKE_como_el_frontend(Enum valor, string esperado)
    {
        var opciones = new JsonSerializerOptions();
        Servicios.ConfigurarJson(opciones);
        Assert.Equal($"\"{esperado}\"", JsonSerializer.Serialize(valor, valor.GetType(), opciones));

        var texto = typeof(NombreEnum).GetMethod(nameof(NombreEnum.Texto))!.MakeGenericMethod(valor.GetType()).Invoke(null, [valor]);
        Assert.Equal(esperado, texto);
    }

    [Fact]
    public void Las_horas_viajan_como_HH_mm()
    {
        var opciones = new JsonSerializerOptions();
        Servicios.ConfigurarJson(opciones);
        Assert.Equal("\"07:30\"", JsonSerializer.Serialize(new TimeOnly(7, 30), opciones));
        Assert.Equal(new TimeOnly(13, 5), JsonSerializer.Deserialize<TimeOnly>("\"13:05\"", opciones));
    }

    [Theory]
    [InlineData("https://a.app, https://b.app/", new[] { "https://a.app", "https://b.app" })]
    public void Los_origenes_CORS_aceptan_texto_separado_por_comas(string valor, string[] esperado)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Cors:Origenes"] = valor }).Build();
        Assert.Equal(esperado, Servicios.OrigenesCors(config));
    }
}
