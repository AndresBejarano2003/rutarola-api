using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RutaRola.Application.Comun;
using RutaRola.Application.Operacion;
using RutaRola.Application.Personas;
using RutaRola.Tests.Infraestructura;

namespace RutaRola.Tests.Operacion;

[Collection(ColeccionBaseDatos.Nombre)]
public class RutasAsignacionesTests(BaseDatosPrueba bd)
{
    [Fact]
    public async Task Crea_una_ruta_con_sus_estudiantes_en_orden_y_la_filtra()
    {
        var admin = await bd.AdminNuevoAsync();
        var c = admin.Cliente;
        var (colegio, acudiente, e1) = await c.CrearEstudianteAsync();
        var e2 = await c.CrearAsync<EstudianteDto>("estudiantes", DatosCatalogos.Estudiante(acudiente.Id, colegio.Id, primerNombre: "Ana"));

        var ruta = await c.CrearAsync<RutaDto>("rutas", DatosOperacion.Ruta(colegio.Id, [e2.Id, e1.Id], "r-01"));
        Assert.Equal([e2.Id, e1.Id], ruta.EstudianteIds);
        Assert.Equal("ACTIVO", ruta.Estado);

        var editada = await (await c.PutAsJsonAsync($"/api/v1/rutas/{ruta.Id}", DatosOperacion.Ruta(colegio.Id, [e1.Id], "r-01"), ClienteApi.Json))
            .LeerAsync<RutaDto>();
        Assert.Equal([e1.Id], editada.EstudianteIds);

        var porEstudiante = await (await c.GetAsync($"/api/v1/rutas?estudianteId={e1.Id}")).LeerAsync<ListaPaginada<RutaDto>>();
        Assert.Equal(ruta.Id, Assert.Single(porEstudiante.Items).Id);
        Assert.Empty((await (await c.GetAsync($"/api/v1/rutas?estudianteId={e2.Id}")).LeerAsync<ListaPaginada<RutaDto>>()).Items);
        Assert.Single((await (await c.GetAsync("/api/v1/rutas?texto=granjas&jornada=Mañana")).LeerAsync<ListaPaginada<RutaDto>>()).Items);
        var opciones = await (await c.GetAsync("/api/v1/rutas/opciones")).LeerAsync<List<Opcion>>();
        Assert.Equal("r-01 · Norte", Assert.Single(opciones).Nombre);

        // El estudiante en ruta no se puede eliminar.
        var motivo = await (await c.GetAsync($"/api/v1/estudiantes/{e1.Id}/motivo-no-eliminar")).LeerAsync<RespuestaMotivo>();
        Assert.Equal("No se puede eliminar: está en la ruta r-01 · Norte. Quítalo de la ruta o cámbialo a Inactivo.", motivo.Motivo);
    }

    [Fact]
    public async Task Valida_codigo_unico_colegio_de_los_estudiantes_y_choque_de_jornada()
    {
        var admin = await bd.AdminNuevoAsync();
        var c = admin.Cliente;
        var (colegio, acudiente, estudiante) = await c.CrearEstudianteAsync();
        await c.CrearAsync<RutaDto>("rutas", DatosOperacion.Ruta(colegio.Id, [estudiante.Id], "R-01"));

        var duplicada = await c.PostAsJsonAsync("/api/v1/rutas", DatosOperacion.Ruta(colegio.Id, [], "r-01"), ClienteApi.Json);
        Assert.Equal("Ya existe una ruta con el código r-01.", await duplicada.MensajeErrorAsync(HttpStatusCode.Conflict));

        var choque = await c.PostAsJsonAsync("/api/v1/rutas", DatosOperacion.Ruta(colegio.Id, [estudiante.Id], "R-02"), ClienteApi.Json);
        Assert.Equal("Uno o más estudiantes ya están en la ruta R-01 · Norte (jornada Mañana). Quítalos de esa ruta primero.",
            await choque.MensajeErrorAsync(HttpStatusCode.UnprocessableEntity));
        // Otra jornada o la ruta inactiva sí se permiten.
        await c.CrearAsync<RutaDto>("rutas", DatosOperacion.Ruta(colegio.Id, [estudiante.Id], "R-03", jornada: "Tarde"));
        await c.CrearAsync<RutaDto>("rutas", DatosOperacion.Ruta(colegio.Id, [estudiante.Id], "R-04", estado: "INACTIVO"));

        var otroColegio = await c.CrearColegioAsync();
        var mal = await c.PostAsJsonAsync("/api/v1/rutas", DatosOperacion.Ruta(otroColegio.Id, [estudiante.Id], "R-05", jornada: "Única"), ClienteApi.Json);
        Assert.Equal("Todos los estudiantes de la ruta deben ser del Colegio San Cayetano.", await mal.MensajeErrorAsync(HttpStatusCode.UnprocessableEntity));

        var inexistente = await c.PostAsJsonAsync("/api/v1/rutas", DatosOperacion.Ruta(colegio.Id, [Guid.NewGuid()], "R-06"), ClienteApi.Json);
        Assert.Equal("Uno o más estudiantes no existen o fueron eliminados.", await inexistente.MensajeErrorAsync(HttpStatusCode.NotFound));

        var invalida = await c.PostAsJsonAsync("/api/v1/rutas", DatosOperacion.Ruta(colegio.Id, [], "R 07", jornada: "Madrugada"), ClienteApi.Json);
        Assert.Equal(HttpStatusCode.BadRequest, invalida.StatusCode);
        var errores = JsonDocument.Parse(await invalida.Content.ReadAsStringAsync()).RootElement.GetProperty("errors");
        Assert.True(errores.TryGetProperty("codigo", out _));
        Assert.True(errores.TryGetProperty("jornada", out _));
    }

    [Fact]
    public async Task Crea_una_asignacion_sin_monitora_ni_fecha_fin_y_bloquea_eliminar_la_ruta()
    {
        var admin = await bd.AdminNuevoAsync();
        var c = admin.Cliente;
        var (colegio, _, estudiante) = await c.CrearEstudianteAsync();
        var ruta = await c.CrearAsync<RutaDto>("rutas", DatosOperacion.Ruta(colegio.Id, [estudiante.Id], "R-01"));
        var vehiculo = await c.CrearAsync<VehiculoDto>("vehiculos", DatosCatalogos.Vehiculo(DatosOperacion.Placa(), "1"));
        var conductor = await c.CrearAsync<EmpleadoDto>("empleados", DatosCatalogos.Empleado(ClienteApi.Unico("c")));

        var respuesta = await c.PostAsJsonAsync("/api/v1/asignaciones", DatosOperacion.Asignacion(ruta.Id, vehiculo.Id, conductor.Id, null), ClienteApi.Json);
        var json = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        Assert.Equal("", json.GetProperty("monitoraId").GetString());
        Assert.Equal("", json.GetProperty("fechaFin").GetString());
        var id = json.GetProperty("id").GetGuid();

        var motivo = await (await c.GetAsync($"/api/v1/rutas/{ruta.Id}/motivo-no-eliminar")).LeerAsync<RespuestaMotivo>();
        Assert.Equal("No se puede eliminar: la ruta tiene 1 asignación(es) de vehículo y conductor. Cámbiala a Inactiva.", motivo.Motivo);
        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"/api/v1/rutas/{ruta.Id}")).StatusCode);

        Assert.Single((await (await c.GetAsync($"/api/v1/asignaciones?empleadoId={conductor.Id}&estado=ACTIVO")).LeerAsync<ListaPaginada<AsignacionDto>>()).Items);
        var opciones = await (await c.GetAsync("/api/v1/asignaciones/opciones")).LeerAsync<List<Opcion>>();
        Assert.Equal($"R-01 · Norte · {DatosOperacion.Hoy.AddDays(-30):yyyy-MM-dd}", Assert.Single(opciones).Nombre);

        // Sin recorridos se puede eliminar.
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/v1/asignaciones/{id}")).StatusCode);
    }

    [Fact]
    public async Task Valida_las_reglas_de_la_asignacion()
    {
        var admin = await bd.AdminNuevoAsync();
        var c = admin.Cliente;
        var (colegio, acudiente, e1) = await c.CrearEstudianteAsync();
        var ruta = await c.CrearAsync<RutaDto>("rutas", DatosOperacion.Ruta(colegio.Id, [e1.Id], "R-01"));
        var vehiculo = await c.CrearAsync<VehiculoDto>("vehiculos", DatosCatalogos.Vehiculo("ABC123", "1"));
        var conductor = await c.CrearAsync<EmpleadoDto>("empleados", DatosCatalogos.Empleado(ClienteApi.Unico("c")));
        var monitora = await c.CrearAsync<EmpleadoDto>("empleados", DatosCatalogos.Empleado(ClienteApi.Unico("m"), "MONITORA"));

        async Task<string> Falla(object solicitud, HttpStatusCode codigo) =>
            await (await c.PostAsJsonAsync("/api/v1/asignaciones", solicitud, ClienteApi.Json)).MensajeErrorAsync(codigo);

        Assert.Equal("El empleado elegido como conductor no es conductor.",
            await Falla(DatosOperacion.Asignacion(ruta.Id, vehiculo.Id, monitora.Id, null), HttpStatusCode.UnprocessableEntity));
        Assert.Equal("El empleado elegido como monitora no es monitora.",
            await Falla(DatosOperacion.Asignacion(ruta.Id, vehiculo.Id, conductor.Id, conductor.Id), HttpStatusCode.UnprocessableEntity));
        Assert.Equal("El vehículo no existe o fue eliminado.",
            await Falla(DatosOperacion.Asignacion(ruta.Id, Guid.NewGuid(), conductor.Id, null), HttpStatusCode.NotFound));

        var vencida = await c.CrearAsync<EmpleadoDto>("empleados",
            DatosCatalogos.Empleado(ClienteApi.Unico("v"), vencimientoLicencia: DatosOperacion.Hoy.AddDays(-1).ToString("yyyy-MM-dd")));
        Assert.Equal("La licencia de José Luis Cárdenas está vencida. Actualízala en Personas › Empleados.",
            await Falla(DatosOperacion.Asignacion(ruta.Id, vehiculo.Id, vencida.Id, null), HttpStatusCode.UnprocessableEntity));

        var soatVencido = await c.CrearAsync<VehiculoDto>("vehiculos",
            DatosCatalogos.Vehiculo("XYZ987", "2", DatosOperacion.Hoy.AddDays(-1).ToString("yyyy-MM-dd")));
        Assert.Equal("El vehículo XYZ987 tiene vencido: SOAT.",
            await Falla(DatosOperacion.Asignacion(ruta.Id, soatVencido.Id, conductor.Id, null), HttpStatusCode.UnprocessableEntity));

        var pequeno = (Dictionary<string, object?>)JsonSerializer.Deserialize<Dictionary<string, object?>>(
            JsonSerializer.Serialize(DatosCatalogos.Vehiculo("PEQ001", "3")))!;
        pequeno["capacidad"] = 1;
        var vehiculoPequeno = await c.CrearAsync<VehiculoDto>("vehiculos", pequeno);
        var e2 = await c.CrearAsync<EstudianteDto>("estudiantes", DatosCatalogos.Estudiante(acudiente.Id, colegio.Id, primerNombre: "Ana"));
        await c.PutAsJsonAsync($"/api/v1/rutas/{ruta.Id}", DatosOperacion.Ruta(colegio.Id, [e1.Id, e2.Id], "R-01"), ClienteApi.Json);
        Assert.Equal("El vehículo PEQ001 tiene 1 puestos y la ruta lleva 2 estudiantes.",
            await Falla(DatosOperacion.Asignacion(ruta.Id, vehiculoPequeno.Id, conductor.Id, null), HttpStatusCode.UnprocessableEntity));

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/v1/asignaciones",
            DatosOperacion.Asignacion(ruta.Id, vehiculo.Id, conductor.Id, null, "2026-05-10", "2026-05-01"), ClienteApi.Json)).StatusCode);

        // Cruces: una activa por ruta; nadie en dos rutas de la misma jornada.
        await c.CrearAsync<AsignacionDto>("asignaciones", DatosOperacion.Asignacion(ruta.Id, vehiculo.Id, conductor.Id, monitora.Id));
        Assert.Equal("La ruta R-01 · Norte ya tiene una asignación activa. Inactívala primero.",
            await Falla(DatosOperacion.Asignacion(ruta.Id, vehiculo.Id, conductor.Id, null), HttpStatusCode.Conflict));
        var (colegio2, _, e3) = await c.CrearEstudianteAsync();
        var ruta2 = await c.CrearAsync<RutaDto>("rutas", DatosOperacion.Ruta(colegio2.Id, [e3.Id], "R-02"));
        var otroConductor = await c.CrearAsync<EmpleadoDto>("empleados", DatosCatalogos.Empleado(ClienteApi.Unico("o")));
        var otroVehiculo = await c.CrearAsync<VehiculoDto>("vehiculos", DatosCatalogos.Vehiculo("OTR456", "4"));
        Assert.Equal("El vehículo ya cubre la ruta R-01 · Norte en la jornada Mañana.",
            await Falla(DatosOperacion.Asignacion(ruta2.Id, vehiculo.Id, otroConductor.Id, null), HttpStatusCode.Conflict));
        Assert.Equal("José Luis Cárdenas ya está asignado a la ruta R-01 · Norte en la jornada Mañana.",
            await Falla(DatosOperacion.Asignacion(ruta2.Id, otroVehiculo.Id, conductor.Id, null), HttpStatusCode.Conflict));
        Assert.Equal("José Luis Cárdenas ya está asignada a la ruta R-01 · Norte en la jornada Mañana.",
            await Falla(DatosOperacion.Asignacion(ruta2.Id, otroVehiculo.Id, otroConductor.Id, monitora.Id), HttpStatusCode.Conflict));
        // Inactiva no choca.
        await c.CrearAsync<AsignacionDto>("asignaciones",
            DatosOperacion.Asignacion(ruta2.Id, vehiculo.Id, conductor.Id, null, estado: "INACTIVO"));
    }

    [Fact]
    public async Task A_no_ve_ni_usa_rutas_o_asignaciones_de_B()
    {
        var a = await bd.AdminNuevoAsync();
        var b = await bd.EscenarioAsync(estudiantes: 1);

        Assert.Empty((await (await a.Cliente.GetAsync("/api/v1/rutas")).LeerAsync<ListaPaginada<RutaDto>>()).Items);
        Assert.Empty((await (await a.Cliente.GetAsync("/api/v1/asignaciones")).LeerAsync<ListaPaginada<AsignacionDto>>()).Items);
        Assert.Equal(HttpStatusCode.NotFound, (await a.Cliente.GetAsync($"/api/v1/rutas/{b.Ruta.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.Cliente.DeleteAsync($"/api/v1/asignaciones/{b.Asignacion.Id}")).StatusCode);

        // A no puede meter estudiantes ni vehículos de B en sus registros.
        var colegioA = await a.Cliente.CrearColegioAsync();
        var conEstudianteB = await a.Cliente.PostAsJsonAsync("/api/v1/rutas", DatosOperacion.Ruta(colegioA.Id, [b.Estudiantes[0].Id]), ClienteApi.Json);
        Assert.Equal(HttpStatusCode.NotFound, conEstudianteB.StatusCode);
        var conductorA = await a.Cliente.CrearAsync<EmpleadoDto>("empleados", DatosCatalogos.Empleado(ClienteApi.Unico("c")));
        var conRutaB = await a.Cliente.PostAsJsonAsync("/api/v1/asignaciones",
            DatosOperacion.Asignacion(b.Ruta.Id, b.Vehiculo.Id, conductorA.Id, null), ClienteApi.Json);
        Assert.Equal("La ruta no existe o fue eliminada.", await conRutaB.MensajeErrorAsync(HttpStatusCode.NotFound));
    }

    [Fact]
    public async Task El_conductor_no_gestiona_rutas_ni_asignaciones()
    {
        var e = await bd.EscenarioAsync(estudiantes: 1);
        Assert.Equal(HttpStatusCode.Forbidden, (await e.ClienteConductor.GetAsync("/api/v1/rutas")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await e.ClienteConductor.GetAsync("/api/v1/asignaciones")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await e.ClienteConductor.DeleteAsync($"/api/v1/asignaciones/{e.Asignacion.Id}")).StatusCode);
    }
}
