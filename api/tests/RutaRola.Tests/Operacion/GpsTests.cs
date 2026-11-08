using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RutaRola.Application.Operacion;
using RutaRola.Tests.Infraestructura;

namespace RutaRola.Tests.Operacion;

/// <summary>Lotes GPS, última posición, trayecto, mapa del administrador y avisos por SignalR.</summary>
[Collection(ColeccionBaseDatos.Nombre)]
public class GpsTests(BaseDatosPrueba bd)
{
    private static object Punto(DateTime fecha, decimal latitud = 2.9273m, decimal longitud = -75.2819m) =>
        new { latitud, longitud, velocidad = 32.5m, rumbo = 90m, fecha };

    private static async Task<ResultadoLoteGps> EnviarAsync(HttpClient cliente, Guid recorrido, params object[] posiciones) =>
        await (await cliente.PostAsJsonAsync($"/api/v1/recorridos/{recorrido}/gps", new { posiciones }, ClienteApi.Json)).LeerAsync<ResultadoLoteGps>();

    private static string Token(HttpClient cliente) => cliente.DefaultRequestHeaders.Authorization!.Parameter!;

    private async Task<(DatosOperacion.Escenario E, RecorridoDto R)> EnCursoAsync(int estudiantes = 1)
    {
        var e = await bd.EscenarioAsync(estudiantes);
        var r = await (await e.ClienteConductor.IniciarAsync(e.Asignacion.Id)).LeerAsync<RecorridoDto>(HttpStatusCode.Created);
        return (e, r);
    }

    [Fact]
    public async Task Registra_un_lote_y_descarta_repetidas_y_fuera_del_periodo()
    {
        var (e, r) = await EnCursoAsync();
        var ahora = DateTime.UtcNow;

        var lote = await EnviarAsync(e.ClienteConductor, r.Id,
            Punto(ahora.AddSeconds(-20), 2.9270m), Punto(ahora.AddSeconds(-10), 2.9271m), Punto(ahora.AddSeconds(-10), 2.9999m),
            Punto(ahora.AddHours(-2)), Punto(ahora.AddHours(1)));
        Assert.Equal(2, lote.Recibidas);
        Assert.Equal(3, lote.Descartadas);
        Assert.Equal(2.9271m, lote.Ultima!.Latitud);
        Assert.Equal(e.Vehiculo.Placa, lote.Ultima.Vehiculo);
        Assert.Equal(e.Vehiculo.Id, lote.Ultima.VehiculoId);

        // Reenviar el mismo lote (reintento sin respuesta) no duplica; la monitora también reporta.
        var reintento = await EnviarAsync(e.ClienteConductor, r.Id, Punto(ahora.AddSeconds(-20)), Punto(ahora.AddSeconds(-10)));
        Assert.Equal(0, reintento.Recibidas);
        Assert.Equal(1, (await EnviarAsync(e.ClienteMonitora!, r.Id, Punto(ahora.AddSeconds(-5), 2.9272m))).Recibidas);

        var trayecto = await (await e.ClienteConductor.GetAsync($"/api/v1/recorridos/{r.Id}/gps")).LeerAsync<List<PosicionDto>>();
        Assert.Equal([2.9270m, 2.9271m, 2.9272m], trayecto.Select(p => p.Latitud));
        var desde = Uri.EscapeDataString(trayecto[1].Fecha.ToString("O"));
        Assert.Single(await (await e.Admin.Cliente.GetAsync($"/api/v1/recorridos/{r.Id}/gps?desde={desde}")).LeerAsync<List<PosicionDto>>());

        var json = JsonDocument.Parse(await (await e.Admin.Cliente.GetAsync($"/api/v1/recorridos/{r.Id}/gps/ultima")).Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(2.9272m, json.GetProperty("latitud").GetDecimal());
        Assert.Equal(32.5m, json.GetProperty("velocidad").GetDecimal());
    }

    [Fact]
    public async Task Valida_el_lote()
    {
        var (e, r) = await EnCursoAsync();
        async Task<JsonElement> Errores(object cuerpo)
        {
            var respuesta = await e.ClienteConductor.PostAsJsonAsync($"/api/v1/recorridos/{r.Id}/gps", cuerpo, ClienteApi.Json);
            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
            return JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement.GetProperty("errors");
        }

        Assert.True((await Errores(new { posiciones = Array.Empty<object>() })).TryGetProperty("posiciones", out _));
        var fuera = await Errores(new { posiciones = new[] { new { latitud = 95, longitud = -200, velocidad = 400, rumbo = 400, fecha = DateTime.UtcNow } } });
        Assert.Equal(["posiciones[0].latitud", "posiciones[0].longitud", "posiciones[0].rumbo", "posiciones[0].velocidad"], fuera.EnumerateObject().Select(p => p.Name).Order());
        var base0 = DateTime.UtcNow;
        var muchas = Enumerable.Range(0, 501).Select(i => Punto(base0.AddMilliseconds(-i * 500))).ToArray();
        Assert.True((await Errores(new { posiciones = muchas })).TryGetProperty("posiciones", out _));
        // 500 lecturas cada 0,5 s: se acepta el lote, pero solo se guarda una cada SeparacionMinima (4 s).
        var lote = await EnviarAsync(e.ClienteConductor, r.Id, muchas[..500]);
        Assert.Equal((63, 437), (lote.Recibidas, lote.Descartadas));
    }

    [Fact]
    public async Task Conductor_y_monitora_reportan_a_la_vez_sin_duplicar_la_ubicacion()
    {
        var (e, r) = await EnCursoAsync();
        var t = DateTime.UtcNow.AddSeconds(-60);

        Assert.Equal(1, (await EnviarAsync(e.ClienteConductor, r.Id, Punto(t, 1m))).Recibidas);
        // La monitora toma la misma ubicación 2 s después: sobra. 10 s después, sí cuenta.
        var monitora = await EnviarAsync(e.ClienteMonitora!, r.Id, Punto(t.AddSeconds(2), 1.1m), Punto(t.AddSeconds(10), 2m));
        Assert.Equal((1, 1), (monitora.Recibidas, monitora.Descartadas));
        // El teléfono de la monitora se apaga: el conductor sigue y la ubicación se mantiene al día.
        Assert.Equal(2, (await EnviarAsync(e.ClienteConductor, r.Id, Punto(t.AddSeconds(20), 3m), Punto(t.AddSeconds(30), 4m))).Recibidas);

        var trayecto = await (await e.Admin.Cliente.GetAsync($"/api/v1/recorridos/{r.Id}/gps")).LeerAsync<List<PosicionDto>>();
        Assert.Equal([1m, 2m, 3m, 4m], trayecto.Select(p => p.Latitud));
    }

    [Fact]
    public async Task Solo_reporta_el_personal_del_recorrido_y_tras_finalizar_solo_lecturas_previas()
    {
        var (e, r) = await EnCursoAsync();
        var otro = await bd.EscenarioAsync(estudiantes: 1);
        var punto = new { posiciones = new[] { Punto(DateTime.UtcNow) } };

        Assert.Equal("El recorrido no existe o no pertenece a tus rutas.",
            await (await otro.ClienteConductor.PostAsJsonAsync($"/api/v1/recorridos/{r.Id}/gps", punto, ClienteApi.Json)).MensajeErrorAsync(HttpStatusCode.NotFound));
        Assert.Equal(HttpStatusCode.Forbidden, (await e.Admin.Cliente.PostAsJsonAsync($"/api/v1/recorridos/{r.Id}/gps", punto, ClienteApi.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otro.ClienteConductor.GetAsync($"/api/v1/recorridos/{r.Id}/gps/ultima")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otro.Admin.Cliente.GetAsync($"/api/v1/recorridos/{r.Id}/gps")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await e.Admin.Cliente.GetAsync($"/api/v1/recorridos/{r.Id}/gps/ultima")).StatusCode);

        var antesDelFin = DateTime.UtcNow;
        await e.ClienteConductor.MarcarAsync(r.Id, "AUSENTE", e.Estudiantes[0].Id);
        (await e.ClienteConductor.PostAsJsonAsync($"/api/v1/recorridos/{r.Id}/finalizar", new { observaciones = "" }, ClienteApi.Json)).EnsureSuccessStatusCode();
        // Llegan al recuperar la señal: la tomada antes del fin se guarda; una muy posterior, no.
        var tarde = await EnviarAsync(e.ClienteConductor, r.Id, Punto(antesDelFin), Punto(DateTime.UtcNow.AddMinutes(30)));
        Assert.Equal((1, 1), (tarde.Recibidas, tarde.Descartadas));
    }

    [Fact]
    public async Task El_acudiente_ve_la_ubicacion_solo_del_recorrido_de_su_estudiante_mientras_esta_en_curso()
    {
        var (e, r) = await EnCursoAsync();
        await EnviarAsync(e.ClienteConductor, r.Id, Punto(DateTime.UtcNow));
        var acudiente = await bd.ClienteDeAsync(e.Acudiente.Usuario, "Clave-123");

        var ultima = await (await acudiente.GetAsync($"/api/v1/recorridos/{r.Id}/gps/ultima")).LeerAsync<PosicionDto>();
        Assert.Equal(r.Id, ultima.RecorridoId);
        Assert.Equal(HttpStatusCode.Forbidden, (await acudiente.GetAsync($"/api/v1/recorridos/{r.Id}/gps")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await acudiente.GetAsync("/api/v1/recorridos/en-curso")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await acudiente.GetAsync($"/api/v1/recorridos/{r.Id}")).StatusCode);

        // Otro acudiente de la misma empresa, sin estudiantes en ese recorrido.
        var otro = await e.Admin.Cliente.CrearAcudienteAsync();
        var clienteOtro = await bd.ClienteDeAsync(otro.Usuario, "Clave-123");
        Assert.Equal(HttpStatusCode.NotFound, (await clienteOtro.GetAsync($"/api/v1/recorridos/{r.Id}/gps/ultima")).StatusCode);

        await e.ClienteConductor.MarcarAsync(r.Id, "AUSENTE", e.Estudiantes[0].Id);
        (await e.ClienteConductor.PostAsJsonAsync($"/api/v1/recorridos/{r.Id}/finalizar", new { observaciones = "" }, ClienteApi.Json)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await acudiente.GetAsync($"/api/v1/recorridos/{r.Id}/gps/ultima")).StatusCode);
    }

    [Fact]
    public async Task El_administrador_ve_los_recorridos_en_curso_de_su_empresa_con_su_posicion()
    {
        var (e, r) = await EnCursoAsync(estudiantes: 2);
        var b = await bd.EscenarioAsync(estudiantes: 1);
        await (await b.ClienteConductor.IniciarAsync(b.Asignacion.Id)).LeerAsync<RecorridoDto>(HttpStatusCode.Created);

        var vacio = Assert.Single(await (await e.Admin.Cliente.GetAsync("/api/v1/recorridos/en-curso")).LeerAsync<List<PosicionRecorridoDto>>());
        Assert.Null(vacio.Posicion);
        Assert.Equal(new ConteoParadasDto(2, 2, 0, 0, 0), vacio.Conteo);

        var ahora = DateTime.UtcNow;
        await EnviarAsync(e.ClienteConductor, r.Id, Punto(ahora.AddSeconds(-30), 1m), Punto(ahora, 2m));
        var mapa = Assert.Single(await (await e.Admin.Cliente.GetAsync("/api/v1/recorridos/en-curso")).LeerAsync<List<PosicionRecorridoDto>>());
        Assert.Equal(r.Id, mapa.RecorridoId);
        Assert.Equal(2m, mapa.Posicion!.Latitud);
        Assert.Equal(HttpStatusCode.Forbidden, (await e.ClienteConductor.GetAsync("/api/v1/recorridos/en-curso")).StatusCode);
    }

    [Fact]
    public async Task El_hub_avisa_posiciones_y_cambios_a_quien_sigue_el_recorrido()
    {
        var (e, r) = await EnCursoAsync();
        await using var monitora = await HubPrueba.ConectarAsync(bd, Token(e.ClienteMonitora!));
        Assert.Null(await monitora.InvocarAsync("Seguir", r.Id));
        await using var admin = await HubPrueba.ConectarAsync(bd, Token(e.Admin.Cliente));
        Assert.Null(await admin.InvocarAsync("SeguirEmpresa"));

        await EnviarAsync(e.ClienteConductor, r.Id, Punto(DateTime.UtcNow, 2.5m));
        Assert.Equal(2.5m, (await monitora.EsperarAsync("posicion")).GetProperty("latitud").GetDecimal());
        Assert.Equal(r.Id, (await admin.EsperarAsync("posicion")).GetProperty("recorridoId").GetGuid());

        await e.ClienteConductor.MarcarAsync(r.Id, "A_BORDO", e.Estudiantes[0].Id);
        Assert.Equal(r.Id, (await monitora.EsperarAsync("recorridoActualizado")).GetProperty("recorridoId").GetGuid());

        // Nadie ajeno puede seguirlo; el conductor no puede ver toda la empresa.
        var otro = await bd.EscenarioAsync(estudiantes: 1);
        await using var ajeno = await HubPrueba.ConectarAsync(bd, Token(otro.ClienteConductor));
        Assert.Contains("no existe o no pertenece", await ajeno.InvocarAsync("Seguir", r.Id));
        Assert.Contains("No tienes permiso", await ajeno.InvocarAsync("SeguirEmpresa"));
        await using var otroAdmin = await HubPrueba.ConectarAsync(bd, Token(otro.Admin.Cliente));
        Assert.Contains("no existe o no pertenece", await otroAdmin.InvocarAsync("Seguir", r.Id));
    }

    [Fact]
    public async Task El_hub_exige_sesion()
    {
        var sinToken = await bd.Api.CreateClient().PostAsync("/hubs/recorridos/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Unauthorized, sinToken.StatusCode);
        var (e, _) = await EnCursoAsync();
        var conToken = await e.ClienteConductor.PostAsync("/hubs/recorridos/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.OK, conToken.StatusCode);
    }

    [Fact]
    public async Task El_acudiente_recibe_por_el_hub_solo_mientras_el_recorrido_esta_en_curso()
    {
        var (e, r) = await EnCursoAsync();
        var acudiente = await bd.ClienteDeAsync(e.Acudiente.Usuario, "Clave-123");
        await using var hub = await HubPrueba.ConectarAsync(bd, Token(acudiente));
        Assert.Null(await hub.InvocarAsync("Seguir", r.Id));
        Assert.Contains("No tienes permiso", await hub.InvocarAsync("SeguirEmpresa"));

        await EnviarAsync(e.ClienteConductor, r.Id, Punto(DateTime.UtcNow, 2.7m));
        Assert.Equal(2.7m, (await hub.EsperarAsync("posicion")).GetProperty("latitud").GetDecimal());

        // Cambios del recorrido de su estudiante le llegan aunque no lo siga (grupo del acudiente).
        await e.ClienteConductor.MarcarAsync(r.Id, "AUSENTE", e.Estudiantes[0].Id);
        var aviso = await hub.EsperarAsync("recorridoActualizado");
        Assert.Equal(r.Id, aviso.GetProperty("recorridoId").GetGuid());
        Assert.Equal("EN_CURSO", aviso.GetProperty("estado").GetString());
        (await e.ClienteConductor.PostAsJsonAsync($"/api/v1/recorridos/{r.Id}/finalizar", new { observaciones = "" }, ClienteApi.Json)).EnsureSuccessStatusCode();
        // Puede llegar dos veces (grupo del recorrido y del acudiente): el cliente lo trata igual.
        while ((await hub.EsperarAsync("recorridoActualizado")).GetProperty("estado").GetString() != "FINALIZADO") { }

        // Finalizado: ya no puede seguirlo.
        await using var tarde = await HubPrueba.ConectarAsync(bd, Token(acudiente));
        Assert.NotNull(await tarde.InvocarAsync("Seguir", r.Id));
    }

    [Fact]
    public async Task La_retencion_borra_las_lecturas_antiguas()
    {
        var (e, r) = await EnCursoAsync();
        await EnviarAsync(e.ClienteConductor, r.Id, Punto(DateTime.UtcNow));
        await using (var db = bd.Contexto(null, modoSistema: true))
        {
            db.GpsVehiculos.Add(new RutaRola.Domain.Operacion.GpsVehiculo
            {
                IdVehiculo = e.Vehiculo.Id, IdViaje = r.Id, Latitud = 1, Longitud = 1,
                FechaRegistro = DateTime.UtcNow.AddDays(-RutaRola.Api.Procesos.ProcesoComunicacion.DiasRetencionGps - 1),
            });
            await db.SaveChangesAsync();
        }

        var proceso = bd.Api.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .OfType<RutaRola.Api.Procesos.ProcesoComunicacion>().Single();
        await proceso.EjecutarAsync(CancellationToken.None);

        await using var verificar = bd.Contexto(null, modoSistema: true);
        Assert.Equal(1, await verificar.GpsVehiculos.CountAsync(g => g.IdViaje == r.Id));
    }
}
