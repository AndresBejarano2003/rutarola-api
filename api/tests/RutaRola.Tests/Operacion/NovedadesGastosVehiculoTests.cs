using System.Net;
using System.Net.Http.Json;
using RutaRola.Application.Comun;
using RutaRola.Application.Comunicacion;
using RutaRola.Application.Operacion;
using RutaRola.Domain.Enums;
using RutaRola.Tests.Infraestructura;

namespace RutaRola.Tests.Operacion;

/// <summary>Novedades del vehículo (reportadas por el conductor/monitora) y gastos del vehículo con su tablero.</summary>
[Collection(ColeccionBaseDatos.Nombre)]
public class NovedadesGastosVehiculoTests(BaseDatosPrueba bd)
{
    private const string Foto = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private static object Novedad(Guid vehiculoId, string gravedad = "ALTA", string descripcion = "Frenos traseros suenan al frenar", object? archivo = null) =>
        new { vehiculoId, tipo = "AVERIA", gravedad, descripcion, kilometraje = 120500, archivo };

    [Fact]
    public async Task El_conductor_reporta_novedades_de_su_vehiculo_y_el_administrador_las_atiende()
    {
        var e = await bd.EscenarioAsync();
        var admin = e.Admin.Cliente;

        // Solo ve y reporta los vehículos de sus asignaciones.
        var mios = await (await e.ClienteConductor.GetAsync("/api/v1/novedades-vehiculo/mis-vehiculos")).LeerAsync<List<Opcion>>();
        Assert.Equal(e.Vehiculo.Id, Assert.Single(mios).Id);
        var otro = await admin.CrearAsync<VehiculoDto>("vehiculos", DatosCatalogos.Vehiculo(DatosOperacion.Placa(), "2"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await e.ClienteConductor.PostAsJsonAsync("/api/v1/novedades-vehiculo/mias", Novedad(otro.Id), ClienteApi.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await e.ClienteConductor.PostAsJsonAsync("/api/v1/novedades-vehiculo/mias", Novedad(e.Vehiculo.Id, descripcion: "corta"), ClienteApi.Json)).StatusCode);
        // El administrador no usa el endpoint del personal, ni el personal el del administrador.
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/novedades-vehiculo/mias")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await e.ClienteConductor.GetAsync("/api/v1/novedades-vehiculo")).StatusCode);

        var novedad = await (await e.ClienteConductor.PostAsJsonAsync("/api/v1/novedades-vehiculo/mias",
            Novedad(e.Vehiculo.Id, archivo: new { nombre = "frenos.png", tipo = "image/png", tamano = 68, url = Foto }), ClienteApi.Json))
            .LeerAsync<NovedadVehiculoDto>(HttpStatusCode.Created);
        Assert.Equal((EstadoNovedadVehiculo.Pendiente, true, 120500), (novedad.Estado, novedad.ReportadaPorMi, novedad.Kilometraje));
        Assert.StartsWith(e.Conductor.PrimerNombre, novedad.Reporta);
        Assert.StartsWith("data:image/png", novedad.Archivo?.Url);

        // La monitora de la misma asignación también la ve (no la reportó ella).
        var deLaMonitora = Assert.Single(await (await e.ClienteMonitora!.GetAsync("/api/v1/novedades-vehiculo/mias")).LeerAsync<List<NovedadVehiculoDto>>());
        Assert.False(deLaMonitora.ReportadaPorMi);
        Assert.Equal("", deLaMonitora.Archivo?.Url); // el listado no trae el contenido

        // El administrador la ve en el vehículo y en la campana (crítica por gravedad alta).
        var delVehiculo = await (await admin.GetAsync($"/api/v1/novedades-vehiculo?vehiculoId={e.Vehiculo.Id}&abiertas=true"))
            .LeerAsync<List<NovedadVehiculoDto>>();
        Assert.Equal(novedad.Id, Assert.Single(delVehiculo).Id);
        await (await admin.GetAsync("/api/v1/notificaciones/resumen")).LeerAsync<ResumenNotificacionesDto>(); // genera las pendientes
        var notificaciones = await (await admin.GetAsync("/api/v1/notificaciones?tamano=100")).LeerAsync<ListaPaginada<NotificacionDto>>();
        var aviso = Assert.Single(notificaciones.Items, n => n.Clave == $"novedad-vehiculo:{novedad.Id}");
        Assert.Equal(NivelNotificacion.Critica, aviso.Nivel);

        // Resolver exige respuesta.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/v1/novedades-vehiculo/{novedad.Id}/estado",
            new { estado = "RESUELTA", respuesta = "" }, ClienteApi.Json)).StatusCode);
        var resuelta = await (await admin.PutAsJsonAsync($"/api/v1/novedades-vehiculo/{novedad.Id}/estado",
            new { estado = "RESUELTA", respuesta = "Se cambiaron las pastillas de freno." }, ClienteApi.Json)).LeerAsync<NovedadVehiculoDto>();
        Assert.Equal((EstadoNovedadVehiculo.Resuelta, "Se cambiaron las pastillas de freno."), (resuelta.Estado, resuelta.Respuesta));
        Assert.NotEqual("", resuelta.Atiende);
        Assert.Empty(await (await admin.GetAsync($"/api/v1/novedades-vehiculo?abiertas=true")).LeerAsync<List<NovedadVehiculoDto>>());
        Assert.Equal(EstadoNovedadVehiculo.Resuelta,
            Assert.Single(await (await e.ClienteConductor.GetAsync("/api/v1/novedades-vehiculo/mias")).LeerAsync<List<NovedadVehiculoDto>>()).Estado);

        // Aislamiento: otra empresa no la ve ni la modifica.
        var otra = await bd.AdminNuevoAsync();
        Assert.Empty(await (await otra.Cliente.GetAsync("/api/v1/novedades-vehiculo")).LeerAsync<List<NovedadVehiculoDto>>());
        Assert.Equal(HttpStatusCode.NotFound, (await otra.Cliente.PutAsJsonAsync($"/api/v1/novedades-vehiculo/{novedad.Id}/estado",
            new { estado = "PENDIENTE" }, ClienteApi.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otra.Cliente.PostAsJsonAsync("/api/v1/novedades-vehiculo",
            Novedad(e.Vehiculo.Id), ClienteApi.Json)).StatusCode);
    }

    private static object Gasto(Guid vehiculoId, string tipo, string fecha, decimal valor, int? kilometraje = null, decimal? cantidad = null) =>
        new { vehiculoId, tipo, fecha, valor, descripcion = "", proveedor = "Terpel", referencia = "", kilometraje, cantidad, archivo = (object?)null };

    [Fact]
    public async Task Los_gastos_del_vehiculo_tienen_historial_y_tablero()
    {
        var admin = await bd.AdminNuevoAsync();
        var c = admin.Cliente;
        var vehiculo = await c.CrearAsync<VehiculoDto>("vehiculos", DatosCatalogos.Vehiculo(DatosOperacion.Placa(), "7"));
        var otro = await c.CrearAsync<VehiculoDto>("vehiculos", DatosCatalogos.Vehiculo(DatosOperacion.Placa(), "8"));
        var hoy = DatosOperacion.Hoy;
        var mesPasado = hoy.AddMonths(-1);
        string F(DateOnly d) => d.ToString("yyyy-MM-dd");

        // Validaciones: combustible sin galones, valor cero, fecha futura.
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/v1/gastos", Gasto(vehiculo.Id, "COMBUSTIBLE", F(hoy), 100_000), ClienteApi.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/v1/gastos", Gasto(vehiculo.Id, "PEAJE", F(hoy), 0), ClienteApi.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await c.PostAsJsonAsync("/api/v1/gastos", Gasto(vehiculo.Id, "PEAJE", F(hoy.AddDays(2)), 10_000), ClienteApi.Json)).StatusCode);

        await c.CrearAsync<GastoVehiculoDto>("gastos", Gasto(vehiculo.Id, "COMBUSTIBLE", F(mesPasado), 200_000, 10_000, 15));
        await c.CrearAsync<GastoVehiculoDto>("gastos", Gasto(vehiculo.Id, "COMBUSTIBLE", F(hoy), 200_000, 10_600, 20));
        var repuesto = await c.CrearAsync<GastoVehiculoDto>("gastos", Gasto(vehiculo.Id, "REPUESTO", F(hoy), 400_000));
        await c.CrearAsync<GastoVehiculoDto>("gastos", Gasto(otro.Id, "LAVADO", F(hoy), 30_000));

        var lista = await (await c.GetAsync($"/api/v1/gastos?vehiculoId={vehiculo.Id}&tamano=50")).LeerAsync<ListaPaginada<GastoVehiculoDto>>();
        Assert.Equal(3, lista.Total);
        Assert.Equal(F(hoy), lista.Items[0].Fecha.ToString("yyyy-MM-dd")); // los más recientes primero

        var resumen = await (await c.GetAsync($"/api/v1/gastos/resumen?vehiculoId={vehiculo.Id}")).LeerAsync<ResumenGastosDto>();
        Assert.Equal((800_000m, 3, 400_000m, 35m), (resumen.Total, resumen.Cantidad, resumen.Combustible, resumen.Galones));
        Assert.Equal(400_000m, resumen.PromedioMensual); // dos meses
        Assert.Equal(600, resumen.KilometrosRecorridos);
        Assert.Equal(30m, resumen.KilometrosPorGalon); // 600 km / 20 galones tanqueados después de la primera lectura
        Assert.Equal(TipoGasto.Combustible, resumen.PorTipo[0].Tipo);
        Assert.Equal(2, resumen.PorMes.Count);

        var todos = await (await c.GetAsync("/api/v1/gastos/resumen")).LeerAsync<ResumenGastosDto>();
        Assert.Equal(830_000m, todos.Total);
        Assert.Equal(2, todos.PorVehiculo.Count);

        // El vehículo con gastos no se elimina; el gasto sí.
        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"/api/v1/vehiculos/{otro.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/v1/gastos/{repuesto.Id}")).StatusCode);

        // Aislamiento.
        var ajena = await bd.AdminNuevoAsync();
        Assert.Equal(0, (await (await ajena.Cliente.GetAsync("/api/v1/gastos")).LeerAsync<ListaPaginada<GastoVehiculoDto>>()).Total);
        Assert.Equal(HttpStatusCode.NotFound, (await ajena.Cliente.PostAsJsonAsync("/api/v1/gastos",
            Gasto(vehiculo.Id, "PEAJE", F(hoy), 10_000), ClienteApi.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ajena.Cliente.DeleteAsync($"/api/v1/gastos/{lista.Items[0].Id}")).StatusCode);
    }
}
