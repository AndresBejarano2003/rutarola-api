using System.Net;
using System.Net.Http.Json;
using RutaRola.Application.Comun;
using RutaRola.Application.Operacion;
using RutaRola.Domain.Enums;
using RutaRola.Tests.Infraestructura;

namespace RutaRola.Tests.Catalogos;

[Collection(ColeccionBaseDatos.Nombre)]
public class VehiculosTests(BaseDatosPrueba bd)
{
    [Fact]
    public async Task Crea_el_vehiculo_con_la_placa_normalizada_y_sus_documentos()
    {
        var admin = await bd.AdminNuevoAsync();

        var vehiculo = await admin.Cliente.CrearAsync<VehiculoDto>("vehiculos", DatosCatalogos.Vehiculo("wtn-482", "101"));

        Assert.Equal("WTN482", vehiculo.Placa);
        Assert.Equal(TipoVehiculo.Buseta, vehiculo.Tipo);
        Assert.Equal(2021, vehiculo.Modelo);
        Assert.Equal(new DateOnly(2030, 1, 31), vehiculo.VencimientoSoat);
        Assert.Equal(new DateOnly(2030, 4, 30), vehiculo.VencimientoPolizas);

        await using var db = bd.Contexto(admin.Empresa.Id);
        Assert.Equal(4, db.DocumentosVehiculo.Count(d => d.IdVehiculo == vehiculo.Id));
    }

    [Fact]
    public async Task Placa_y_numero_interno_son_unicos()
    {
        var admin = await bd.AdminNuevoAsync();
        await admin.Cliente.CrearAsync<VehiculoDto>("vehiculos", DatosCatalogos.Vehiculo("SXK915", "102"));

        var placa = await admin.Cliente.PostAsJsonAsync("/api/v1/vehiculos", DatosCatalogos.Vehiculo("SXK915", "999"), ClienteApi.Json);
        Assert.Equal("Ya existe un vehículo con la placa SXK915.", await placa.MensajeErrorAsync(HttpStatusCode.Conflict));

        var interno = await admin.Cliente.PostAsJsonAsync("/api/v1/vehiculos", DatosCatalogos.Vehiculo("AAA111", "102"), ClienteApi.Json);
        Assert.Equal("El número interno 102 ya está asignado a otro vehículo.", await interno.MensajeErrorAsync(HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task Valida_placa_y_capacidad()
    {
        var admin = await bd.AdminNuevoAsync();

        var respuesta = await admin.Cliente.PostAsJsonAsync("/api/v1/vehiculos", DatosCatalogos.Vehiculo("AB12", "7"), ClienteApi.Json);

        Assert.Equal("La placa debe tener 3 letras y 3 números (ej. ABC123).", await respuesta.MensajeErrorAsync(HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task Ordena_por_numero_interno_numerico_filtra_por_tipo_y_actualiza_documentos()
    {
        var admin = await bd.AdminNuevoAsync();
        await admin.Cliente.CrearAsync<VehiculoDto>("vehiculos", DatosCatalogos.Vehiculo("AAA010", "10"));
        var dos = await admin.Cliente.CrearAsync<VehiculoDto>("vehiculos", DatosCatalogos.Vehiculo("AAA002", "2"));

        var lista = await (await admin.Cliente.GetAsync("/api/v1/vehiculos?tipo=BUSETA&pagina=1&tamano=10")).LeerAsync<ListaPaginada<VehiculoDto>>();
        Assert.Equal(["2", "10"], lista.Items.Select(v => v.NumeroInterno));
        Assert.Empty((await (await admin.Cliente.GetAsync("/api/v1/vehiculos?tipo=BUS")).LeerAsync<ListaPaginada<VehiculoDto>>()).Items);

        var opciones = await (await admin.Cliente.GetAsync("/api/v1/vehiculos/opciones")).LeerAsync<List<Opcion>>();
        Assert.Equal("AAA002 · Interno 2 · Chevrolet", opciones[0].Nombre);

        var editado = await (await admin.Cliente.PutAsJsonAsync($"/api/v1/vehiculos/{dos.Id}",
            DatosCatalogos.Vehiculo("AAA002", "2", vencimiento: "2031-12-31"), ClienteApi.Json)).LeerAsync<VehiculoDto>();
        Assert.Equal(new DateOnly(2031, 12, 31), editado.VencimientoSoat);
        await using var db = bd.Contexto(admin.Empresa.Id);
        Assert.Equal(4, db.DocumentosVehiculo.Count(d => d.IdVehiculo == dos.Id));
    }

    [Fact]
    public async Task Se_elimina_si_no_tiene_asignaciones()
    {
        var admin = await bd.AdminNuevoAsync();
        var vehiculo = await admin.Cliente.CrearAsync<VehiculoDto>("vehiculos", DatosCatalogos.Vehiculo("QHL158", "105"));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.Cliente.DeleteAsync($"/api/v1/vehiculos/{vehiculo.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Cliente.DeleteAsync($"/api/v1/vehiculos/{vehiculo.Id}")).StatusCode);
    }
}
