using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Application.Operacion;
using RutaRola.Domain.Enums;
using RutaRola.Tests.Infraestructura;

namespace RutaRola.Tests.Operacion;

/// <summary>Ruta del día (conductor/monitora) e historial del administrador.</summary>
[Collection(ColeccionBaseDatos.Nombre)]
public class RecorridosTests(BaseDatosPrueba bd)
{
    [Fact]
    public async Task El_conductor_ve_sus_rutas_del_dia_con_su_companera()
    {
        var e = await bd.EscenarioAsync();

        var rutas = await (await e.ClienteConductor.GetAsync("/api/v1/recorridos/del-dia")).LeerAsync<List<RutaAsignadaDto>>();
        var ruta = Assert.Single(rutas);
        Assert.Equal(e.Asignacion.Id, ruta.AsignacionId);
        Assert.Equal(TipoEmpleado.Conductor, ruta.Rol);
        Assert.Equal(TipoEmpleado.Monitora, ruta.Companero!.Rol);
        Assert.Equal(3, ruta.Estudiantes);
        Assert.Equal(e.Vehiculo.Placa, ruta.Vehiculo);
        Assert.Empty(ruta.Recorridos);

        var monitora = Assert.Single(await (await e.ClienteMonitora!.GetAsync("/api/v1/recorridos/del-dia")).LeerAsync<List<RutaAsignadaDto>>());
        Assert.Equal(TipoEmpleado.Monitora, monitora.Rol);

        // Fuera de las fechas de la asignación no hay rutas.
        var antes = DatosOperacion.Hoy.AddDays(-31).ToString("yyyy-MM-dd");
        Assert.Empty(await (await e.ClienteConductor.GetAsync($"/api/v1/recorridos/del-dia?fecha={antes}")).LeerAsync<List<RutaAsignadaDto>>());
        // El administrador no tiene Ruta del día.
        Assert.Equal(HttpStatusCode.Forbidden, (await e.Admin.Cliente.GetAsync("/api/v1/recorridos/del-dia")).StatusCode);
    }

    [Fact]
    public async Task Iniciar_toma_el_snapshot_y_la_companera_se_une_al_mismo_recorrido()
    {
        var e = await bd.EscenarioAsync();

        var inicio = await e.ClienteConductor.IniciarAsync(e.Asignacion.Id);
        var recorrido = await inicio.LeerAsync<RecorridoDto>(HttpStatusCode.Created);
        Assert.Equal(EstadoViaje.EnCurso, recorrido.Estado);
        Assert.Equal(e.Conductor.Id.ToString(), recorrido.IniciadoPor);
        Assert.Equal(e.Monitora!.Id.ToString(), recorrido.MonitoraId);
        Assert.Equal(e.Estudiantes.Select(x => x.Id), recorrido.Paradas.Select(p => p.EstudianteId));
        var parada = recorrido.Paradas[0];
        Assert.Equal("Juan Sebastián Pérez López", parada.Estudiante);
        Assert.Equal("1° · Primero", parada.Grado);
        Assert.Equal("María Pérez López", parada.Acudiente);
        Assert.Equal("3001234567", parada.TelefonoAcudiente);
        Assert.Equal(EstadoParada.Pendiente, parada.Estado);
        Assert.Null(recorrido.OrdenActualizado);

        var union = await e.ClienteMonitora!.IniciarAsync(e.Asignacion.Id);
        Assert.Equal(recorrido.Id, (await union.LeerAsync<RecorridoDto>(HttpStatusCode.OK)).Id);

        var json = JsonDocument.Parse(await (await e.ClienteConductor.GetAsync($"/api/v1/recorridos/{recorrido.Id}")).Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("EN_CURSO", json.GetProperty("estado").GetString());
        Assert.Equal("RECOGIDA", json.GetProperty("trayecto").GetString());
        Assert.Equal("PENDIENTE", json.GetProperty("paradas")[0].GetProperty("estado").GetString());

        // Solo uno en curso por asignación.
        var entrega = await e.ClienteConductor.IniciarAsync(e.Asignacion.Id, "ENTREGA");
        Assert.Equal($"Hay un recorrido en curso en {e.Ruta.Codigo} · Norte. Finalízalo antes de iniciar otro.",
            await entrega.MensajeErrorAsync(HttpStatusCode.Conflict));

        var rutas = await (await e.ClienteMonitora!.GetAsync("/api/v1/recorridos/del-dia")).LeerAsync<List<RutaAsignadaDto>>();
        var resumen = Assert.Single(Assert.Single(rutas).Recorridos);
        Assert.Equal(new ConteoParadasDto(3, 3, 0, 0, 0), resumen.Conteo);

        // Una vez iniciado, la asignación no se elimina.
        var motivo = await (await e.Admin.Cliente.GetAsync($"/api/v1/asignaciones/{e.Asignacion.Id}/motivo-no-eliminar")).LeerAsync<RespuestaMotivo>();
        Assert.Equal("No se puede eliminar: la asignación tiene 1 recorrido(s) registrados. Cámbiala a Inactiva para conservar el historial.", motivo.Motivo);
    }

    [Fact]
    public async Task Un_recorrido_que_quedo_abierto_otro_dia_no_bloquea_el_de_hoy()
    {
        var e = await bd.EscenarioAsync(estudiantes: 1);
        var ayer = await (await e.ClienteConductor.IniciarAsync(e.Asignacion.Id)).LeerAsync<RecorridoDto>(HttpStatusCode.Created);
        await using (var db = bd.Contexto(null, modoSistema: true))
        {
            await db.ViajesRuta.Where(v => v.Id == ayer.Id).ExecuteUpdateAsync(x => x.SetProperty(v => v.FechaViaje, DatosOperacion.Hoy.AddDays(-1)));
        }

        var hoy = await (await e.ClienteConductor.IniciarAsync(e.Asignacion.Id, "ENTREGA")).LeerAsync<RecorridoDto>(HttpStatusCode.Created);
        Assert.NotEqual(ayer.Id, hoy.Id);
        Assert.Equal(DatosOperacion.Hoy, hoy.Fecha);
    }

    [Fact]
    public async Task Solo_inicia_quien_tiene_la_ruta_asignada_y_con_estudiantes_activos()
    {
        var e = await bd.EscenarioAsync(estudiantes: 1);
        var otro = await bd.EscenarioAsync(estudiantes: 1);

        var ajena = await otro.ClienteConductor.IniciarAsync(e.Asignacion.Id);
        Assert.Equal("Esta ruta no está asignada a ti para hoy. Consulta con el administrador.",
            await ajena.MensajeErrorAsync(HttpStatusCode.UnprocessableEntity));
        Assert.Equal(HttpStatusCode.Forbidden, (await e.Admin.Cliente.IniciarAsync(e.Asignacion.Id)).StatusCode);

        var est = (Dictionary<string, object?>)DatosCatalogos.Estudiante(e.Acudiente.Id, e.Colegio.Id, documento: e.Estudiantes[0].NumeroDocumento);
        est["estado"] = "INACTIVO";
        (await e.Admin.Cliente.PutAsJsonAsync($"/api/v1/estudiantes/{e.Estudiantes[0].Id}", est, ClienteApi.Json)).EnsureSuccessStatusCode();
        var vacia = await e.ClienteConductor.IniciarAsync(e.Asignacion.Id);
        Assert.Equal($"{e.Ruta.Codigo} · Norte no tiene estudiantes activos.", await vacia.MensajeErrorAsync(HttpStatusCode.UnprocessableEntity));
    }

    [Fact]
    public async Task Marca_las_paradas_segun_las_transiciones_y_finaliza_sin_nadie_a_bordo()
    {
        var e = await bd.EscenarioAsync();
        var c = e.ClienteConductor;
        var r = await (await c.IniciarAsync(e.Asignacion.Id)).LeerAsync<RecorridoDto>(HttpStatusCode.Created);
        var (juan, valentina, samuel) = (e.Estudiantes[0].Id, e.Estudiantes[1].Id, e.Estudiantes[2].Id);

        var marcado = await c.MarcarAsync(r.Id, "A_BORDO", juan);
        Assert.NotNull(marcado.Paradas[0].HoraAbordo);
        Assert.True(marcado.FechaActualizacion > r.FechaActualizacion || r.FechaActualizacion is null);

        var invalida = await c.PatchAsJsonAsync($"/api/v1/recorridos/{r.Id}/paradas", new { estudianteIds = new[] { valentina }, estado = "ENTREGADO" }, ClienteApi.Json);
        Assert.Equal("Valentina Sebastián Pérez López: no se puede pasar de \"Pendiente\" a \"Entregado\".",
            await invalida.MensajeErrorAsync(HttpStatusCode.UnprocessableEntity));
        var ajeno = await c.PatchAsJsonAsync($"/api/v1/recorridos/{r.Id}/paradas", new { estudianteIds = new[] { Guid.NewGuid() }, estado = "A_BORDO" }, ClienteApi.Json);
        Assert.Equal("El estudiante no hace parte de este recorrido.", await ajeno.MensajeErrorAsync(HttpStatusCode.NotFound));

        await c.MarcarAsync(r.Id, "AUSENTE", samuel);
        var pendiente = await c.PostAsJsonAsync($"/api/v1/recorridos/{r.Id}/finalizar", new { observaciones = "" }, ClienteApi.Json);
        Assert.Equal("Hay 1 estudiante a bordo. Ningún estudiante puede quedar en el vehículo: márcalo como entregado.",
            await pendiente.MensajeErrorAsync(HttpStatusCode.UnprocessableEntity));

        // La monitora marca en el mismo recorrido (y "Deshacer" regresa un paso).
        var m = e.ClienteMonitora!;
        await m.MarcarAsync(r.Id, "A_BORDO", valentina);
        var deshecho = await m.MarcarAsync(r.Id, "PENDIENTE", valentina);
        Assert.Null(deshecho.Paradas[1].HoraAbordo);
        await m.MarcarAsync(r.Id, "ENTREGADO", juan);
        var faltan = await c.PostAsJsonAsync($"/api/v1/recorridos/{r.Id}/finalizar", new { observaciones = "" }, ClienteApi.Json);
        Assert.Equal("Falta 1 estudiante por marcar como recogido o \"No asistió\".", await faltan.MensajeErrorAsync(HttpStatusCode.UnprocessableEntity));

        await m.MarcarAsync(r.Id, "A_BORDO", valentina);
        var todos = await m.MarcarAsync(r.Id, "ENTREGADO", valentina, juan);
        Assert.All(todos.Paradas.Take(2), p => Assert.Equal(EstadoParada.Entregado, p.Estado));

        var fin = await (await c.PostAsJsonAsync($"/api/v1/recorridos/{r.Id}/finalizar", new { observaciones = "Samuel estaba enfermo." }, ClienteApi.Json))
            .LeerAsync<RecorridoDto>();
        Assert.Equal(EstadoViaje.Finalizado, fin.Estado);
        Assert.NotNull(fin.Fin);
        Assert.Equal("Samuel estaba enfermo.", fin.Observaciones);

        var cerrado = await c.PatchAsJsonAsync($"/api/v1/recorridos/{r.Id}/paradas", new { estudianteIds = new[] { samuel }, estado = "PENDIENTE" }, ClienteApi.Json);
        Assert.Equal("Este recorrido ya fue finalizado y no se puede modificar.", await cerrado.MensajeErrorAsync(HttpStatusCode.UnprocessableEntity));

        // Con la recogida finalizada se puede iniciar la entrega, en orden inverso.
        var entrega = await (await c.IniciarAsync(e.Asignacion.Id, "ENTREGA")).LeerAsync<RecorridoDto>(HttpStatusCode.Created);
        Assert.Equal([samuel, valentina, juan], entrega.Paradas.Select(p => p.EstudianteId));

        // Eventos registrados y estudiante en un recorrido no se elimina.
        await using var db = bd.Contexto(null, modoSistema: true);
        var eventos = await db.Eventos.Where(x => x.IdViaje == r.Id).Select(x => x.TipoEvento).ToListAsync();
        Assert.Contains(TipoEvento.Inicio, eventos);
        Assert.Contains(TipoEvento.Finalizacion, eventos);
        Assert.Equal(7, eventos.Count(x => x == TipoEvento.CambioParada));
        var motivo = await (await e.Admin.Cliente.GetAsync($"/api/v1/estudiantes/{samuel}/motivo-no-eliminar")).LeerAsync<RespuestaMotivo>();
        Assert.StartsWith("No se puede eliminar: está en la ruta", motivo.Motivo);
    }

    [Fact]
    public async Task Solo_la_monitora_reordena_las_paradas_por_visitar()
    {
        var e = await bd.EscenarioAsync();
        var r = await (await e.ClienteConductor.IniciarAsync(e.Asignacion.Id)).LeerAsync<RecorridoDto>(HttpStatusCode.Created);
        var (juan, valentina, samuel) = (e.Estudiantes[0].Id, e.Estudiantes[1].Id, e.Estudiantes[2].Id);

        var conductor = await e.ClienteConductor.PutAsJsonAsync($"/api/v1/recorridos/{r.Id}/orden", new { estudianteIds = new[] { samuel, valentina, juan } }, ClienteApi.Json);
        Assert.Equal("Solo la monitora puede cambiar el orden de los estudiantes.", await conductor.MensajeErrorAsync(HttpStatusCode.Forbidden));

        await e.ClienteMonitora!.MarcarAsync(r.Id, "A_BORDO", juan);
        // Juan ya fue visitado: la lista debe ser EXACTAMENTE las por visitar.
        var desactualizada = await e.ClienteMonitora!.PutAsJsonAsync($"/api/v1/recorridos/{r.Id}/orden", new { estudianteIds = new[] { samuel, valentina, juan } }, ClienteApi.Json);
        Assert.Equal("El recorrido cambió mientras ordenabas. Revisa la lista e inténtalo de nuevo.", await desactualizada.MensajeErrorAsync(HttpStatusCode.Conflict));

        var ordenado = await (await e.ClienteMonitora!.PutAsJsonAsync($"/api/v1/recorridos/{r.Id}/orden", new { estudianteIds = new[] { samuel, valentina } }, ClienteApi.Json))
            .LeerAsync<RecorridoDto>();
        Assert.Equal([juan, samuel, valentina], ordenado.Paradas.Select(p => p.EstudianteId));
        Assert.Equal(e.Monitora!.Id, ordenado.OrdenActualizado!.EmpleadoId);
        Assert.Equal("José Luis Cárdenas", ordenado.OrdenActualizado.Nombre);

        // El conductor ve el nuevo orden.
        var visto = await (await e.ClienteConductor.GetAsync($"/api/v1/recorridos/{r.Id}")).LeerAsync<RecorridoDto>();
        Assert.Equal([juan, samuel, valentina], visto.Paradas.Select(p => p.EstudianteId));
    }

    [Fact]
    public async Task Sin_monitora_reordena_el_conductor()
    {
        var e = await bd.EscenarioAsync(estudiantes: 2, conMonitora: false);
        var r = await (await e.ClienteConductor.IniciarAsync(e.Asignacion.Id)).LeerAsync<RecorridoDto>(HttpStatusCode.Created);
        Assert.Equal("", r.MonitoraId);
        var ordenado = await (await e.ClienteConductor.PutAsJsonAsync($"/api/v1/recorridos/{r.Id}/orden",
            new { estudianteIds = new[] { e.Estudiantes[1].Id, e.Estudiantes[0].Id } }, ClienteApi.Json)).LeerAsync<RecorridoDto>();
        Assert.Equal(e.Estudiantes[1].Id, ordenado.Paradas[0].EstudianteId);
    }

    [Fact]
    public async Task Un_empleado_no_ve_recorridos_ajenos_y_el_admin_solo_los_de_su_empresa()
    {
        var e = await bd.EscenarioAsync(estudiantes: 1);
        var r = await (await e.ClienteConductor.IniciarAsync(e.Asignacion.Id)).LeerAsync<RecorridoDto>(HttpStatusCode.Created);

        // Otro conductor de la MISMA empresa.
        var usuario = ClienteApi.Unico("otro");
        await e.Admin.Cliente.CrearAsync<Application.Personas.EmpleadoDto>("empleados", DatosCatalogos.Empleado(usuario));
        var otro = await bd.ClienteDeAsync(usuario, "Clave-123");
        Assert.Equal("El recorrido no pertenece a tus rutas.", await (await otro.GetAsync($"/api/v1/recorridos/{r.Id}")).MensajeErrorAsync(HttpStatusCode.Forbidden));
        var marcar = await otro.PatchAsJsonAsync($"/api/v1/recorridos/{r.Id}/paradas", new { estudianteIds = new[] { e.Estudiantes[0].Id }, estado = "A_BORDO" }, ClienteApi.Json);
        Assert.Equal(HttpStatusCode.NotFound, marcar.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otro.GetAsync("/api/v1/recorridos")).StatusCode);

        // El admin ve el detalle; el de otra empresa no.
        Assert.Equal(r.Id, (await (await e.Admin.Cliente.GetAsync($"/api/v1/recorridos/{r.Id}")).LeerAsync<RecorridoDto>()).Id);
        var b = await bd.AdminNuevoAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await b.Cliente.GetAsync($"/api/v1/recorridos/{r.Id}")).StatusCode);
        Assert.Empty((await (await b.Cliente.GetAsync("/api/v1/recorridos")).LeerAsync<ListaPaginada<RecorridoDto>>()).Items);
        // El conductor de otra empresa tampoco.
        var ajeno = await bd.EscenarioAsync(estudiantes: 1);
        Assert.Equal(HttpStatusCode.NotFound, (await ajeno.ClienteConductor.GetAsync($"/api/v1/recorridos/{r.Id}")).StatusCode);
    }

    [Fact]
    public async Task El_historial_filtra_por_texto_fechas_trayecto_estado_y_novedad()
    {
        var e = await bd.EscenarioAsync(estudiantes: 2);
        var c = e.ClienteConductor;
        var recogida = await (await c.IniciarAsync(e.Asignacion.Id)).LeerAsync<RecorridoDto>(HttpStatusCode.Created);
        await c.MarcarAsync(recogida.Id, "AUSENTE", e.Estudiantes[0].Id);
        await c.MarcarAsync(recogida.Id, "A_BORDO", e.Estudiantes[1].Id);
        await c.MarcarAsync(recogida.Id, "ENTREGADO", e.Estudiantes[1].Id);
        await c.PostAsJsonAsync($"/api/v1/recorridos/{recogida.Id}/finalizar", new { observaciones = "" }, ClienteApi.Json);
        var entrega = await (await c.IniciarAsync(e.Asignacion.Id, "ENTREGA")).LeerAsync<RecorridoDto>(HttpStatusCode.Created);

        async Task<List<Guid>> Buscar(string consulta) =>
            (await (await e.Admin.Cliente.GetAsync($"/api/v1/recorridos?{consulta}")).LeerAsync<ListaPaginada<RecorridoDto>>()).Items.Select(x => x.Id).ToList();

        Assert.Equal([recogida.Id, entrega.Id], (await Buscar("")).Order());
        Assert.Equal([recogida.Id], await Buscar("trayecto=RECOGIDA"));
        Assert.Equal([entrega.Id], await Buscar("estado=EN_CURSO"));
        Assert.Equal([recogida.Id], await Buscar("novedad=AUSENCIAS"));
        Assert.Empty(await Buscar("novedad=OBSERVACIONES"));
        Assert.Equal(2, (await Buscar("texto=valentina")).Count);
        Assert.Equal(2, (await Buscar($"texto={e.Vehiculo.Placa.ToLowerInvariant()}&rutaId={e.Ruta.Id}")).Count);
        Assert.Empty(await Buscar("texto=nadie"));
        var hoy = DatosOperacion.Hoy.ToString("yyyy-MM-dd");
        Assert.Equal(2, (await Buscar($"desde={hoy}&hasta={hoy}")).Count);
        Assert.Empty(await Buscar($"desde={DatosOperacion.Hoy.AddDays(1):yyyy-MM-dd}"));
        Assert.Single(await Buscar("pagina=1&tamano=1"));

        // Conductor y monitora no ven el historial (datos de menores de toda la empresa).
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/v1/recorridos")).StatusCode);
    }
}
