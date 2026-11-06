using System.Net;
using System.Net.Http.Json;
using RutaRola.Application.Operacion;
using RutaRola.Application.Personas;

namespace RutaRola.Tests.Infraestructura;

/// <summary>Rutas, asignaciones y un escenario completo de Ruta del día.</summary>
public static class DatosOperacion
{
    public static DateOnly Hoy => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-5));

    public static Dictionary<string, object?> Ruta(Guid colegioId, IEnumerable<Guid> estudianteIds, string? codigo = null,
        string jornada = "Mañana", string estado = "ACTIVO") => new()
    {
        ["codigo"] = codigo ?? $"R-{Random.Shared.Next(100, 99999)}",
        ["nombre"] = "Norte",
        ["colegioId"] = colegioId,
        ["jornada"] = jornada,
        ["sector"] = "Cándido, Las Granjas",
        ["estudianteIds"] = estudianteIds.ToArray(),
        ["estado"] = estado,
        ["observaciones"] = "",
    };

    public static Dictionary<string, object?> Asignacion(Guid rutaId, Guid vehiculoId, Guid conductorId, Guid? monitoraId,
        string? fechaInicio = null, string fechaFin = "", string estado = "ACTIVO") => new()
    {
        ["rutaId"] = rutaId,
        ["vehiculoId"] = vehiculoId,
        ["conductorId"] = conductorId,
        ["monitoraId"] = monitoraId?.ToString() ?? "",
        ["fechaInicio"] = fechaInicio ?? Hoy.AddDays(-30).ToString("yyyy-MM-dd"),
        ["fechaFin"] = fechaFin,
        ["estado"] = estado,
        ["observaciones"] = "",
    };

    public static string Placa() => $"{(char)('A' + Random.Shared.Next(26))}{(char)('A' + Random.Shared.Next(26))}{(char)('A' + Random.Shared.Next(26))}{Random.Shared.Next(100, 999)}";

    public record Escenario(
        DatosCatalogos.Admin Admin, ColegioDto Colegio, AcudienteDto Acudiente, IReadOnlyList<EstudianteDto> Estudiantes, RutaDto Ruta,
        VehiculoDto Vehiculo, EmpleadoDto Conductor, EmpleadoDto? Monitora, AsignacionDto Asignacion,
        HttpClient ClienteConductor, HttpClient? ClienteMonitora);

    /// <summary>Empresa nueva con una ruta de <paramref name="estudiantes"/> estudiantes asignada a un conductor (+ monitora).</summary>
    public static async Task<Escenario> EscenarioAsync(this BaseDatosPrueba bd, int estudiantes = 3, bool conMonitora = true)
    {
        var admin = await bd.AdminNuevoAsync();
        var c = admin.Cliente;
        var colegio = await c.CrearColegioAsync();
        var acudiente = await c.CrearAcudienteAsync();
        var lista = new List<EstudianteDto>();
        string[] nombres = ["Juan", "Valentina", "Samuel", "Isabella", "Mateo"];
        for (var i = 0; i < estudiantes; i++)
        {
            lista.Add(await c.CrearAsync<EstudianteDto>("estudiantes", DatosCatalogos.Estudiante(acudiente.Id, colegio.Id, primerNombre: nombres[i])));
        }
        var ruta = await c.CrearAsync<RutaDto>("rutas", Ruta(colegio.Id, lista.Select(e => e.Id)));
        var vehiculo = await c.CrearAsync<VehiculoDto>("vehiculos", DatosCatalogos.Vehiculo(Placa(), "1"));
        var usuarioConductor = ClienteApi.Unico("cond");
        var conductor = await c.CrearAsync<EmpleadoDto>("empleados", DatosCatalogos.Empleado(usuarioConductor));
        EmpleadoDto? monitora = null;
        HttpClient? clienteMonitora = null;
        if (conMonitora)
        {
            var usuarioMonitora = ClienteApi.Unico("moni");
            monitora = await c.CrearAsync<EmpleadoDto>("empleados", DatosCatalogos.Empleado(usuarioMonitora, "MONITORA"));
            clienteMonitora = await bd.ClienteDeAsync(usuarioMonitora, "Clave-123");
        }
        var asignacion = await c.CrearAsync<AsignacionDto>("asignaciones", Asignacion(ruta.Id, vehiculo.Id, conductor.Id, monitora?.Id));
        return new Escenario(admin, colegio, acudiente, lista, ruta, vehiculo, conductor, monitora, asignacion,
            await bd.ClienteDeAsync(usuarioConductor, "Clave-123"), clienteMonitora);
    }

    public static async Task<HttpResponseMessage> IniciarAsync(this HttpClient cliente, Guid asignacionId, string trayecto = "RECOGIDA") =>
        await cliente.PostAsJsonAsync("/api/v1/recorridos", new { asignacionId, trayecto }, ClienteApi.Json);

    public static async Task<RecorridoDto> MarcarAsync(this HttpClient cliente, Guid id, string estado, params Guid[] estudianteIds) =>
        await (await cliente.PatchAsJsonAsync($"/api/v1/recorridos/{id}/paradas", new { estudianteIds, estado }, ClienteApi.Json))
            .LeerAsync<RecorridoDto>();

    public static Task<HttpResponseMessage> PatchAsJsonAsync(this HttpClient cliente, string url, object cuerpo, System.Text.Json.JsonSerializerOptions json) =>
        cliente.PatchAsync(url, JsonContent.Create(cuerpo, options: json));
}
