using System.Net;
using System.Net.Http.Json;
using RutaRola.Application.Comun;
using RutaRola.Application.Operacion;
using RutaRola.Application.Personas;
using RutaRola.Domain.Enums;
using RutaRola.Tests.Infraestructura;

namespace RutaRola.Tests.Operacion;

/// <summary>Rutas que atienden varios colegios y el orden de recogida que define el administrador.</summary>
[Collection(ColeccionBaseDatos.Nombre)]
public class RutasVariosColegiosTests(BaseDatosPrueba bd)
{
    [Fact]
    public async Task Una_ruta_lleva_estudiantes_de_varios_colegios_y_el_recorrido_guarda_el_colegio_de_cada_uno()
    {
        var admin = await bd.AdminNuevoAsync();
        var c = admin.Cliente;
        var colegioA = await c.CrearColegioAsync();
        var colegioB = await c.CrearAsync<ColegioDto>("colegios", DatosCatalogos.Colegio(razonSocial: "Liceo Los Andes"));
        var acudiente = await c.CrearAcudienteAsync();
        var a1 = await c.CrearAsync<EstudianteDto>("estudiantes", DatosCatalogos.Estudiante(acudiente.Id, colegioA.Id, primerNombre: "Andrés"));
        var b1 = await c.CrearAsync<EstudianteDto>("estudiantes", DatosCatalogos.Estudiante(acudiente.Id, colegioB.Id, primerNombre: "María"));
        var a2 = await c.CrearAsync<EstudianteDto>("estudiantes", DatosCatalogos.Estudiante(acudiente.Id, colegioA.Id, primerNombre: "Nicoll"));

        var ruta = await c.CrearAsync<RutaDto>("rutas",
            DatosOperacion.Ruta(colegioA.Id, [a1.Id, b1.Id, a2.Id], colegioIds: [colegioA.Id, colegioB.Id]));
        Assert.Equal([colegioA.Id, colegioB.Id], ruta.ColegioIds);
        Assert.Equal(colegioA.Id, ruta.ColegioId);
        Assert.Equal([a1.Id, b1.Id, a2.Id], ruta.EstudianteIds);

        // Filtra por cualquiera de sus colegios; el colegio no se elimina mientras una ruta lo atienda.
        Assert.Single((await (await c.GetAsync($"/api/v1/rutas?colegioId={colegioB.Id}")).LeerAsync<ListaPaginada<RutaDto>>()).Items);
        var motivo = await (await c.GetAsync($"/api/v1/colegios/{colegioB.Id}/motivo-no-eliminar")).LeerAsync<RespuestaMotivo>();
        Assert.Contains("1 ruta(s)", motivo.Motivo);
        // Colegios de una jornada: los que tienen cursos activos en ella.
        Assert.Equal(2, (await (await c.GetAsync("/api/v1/colegios/opciones?jornada=Mañana")).LeerAsync<List<Opcion>>()).Count);
        Assert.Empty(await (await c.GetAsync("/api/v1/colegios/opciones?jornada=Tarde")).LeerAsync<List<Opcion>>());

        // Un estudiante de un colegio que no está en la ruta no entra.
        var sinB = await c.PutAsJsonAsync($"/api/v1/rutas/{ruta.Id}",
            DatosOperacion.Ruta(colegioA.Id, [a1.Id, b1.Id, a2.Id], colegioIds: [colegioA.Id]), ClienteApi.Json);
        Assert.Equal("Todos los estudiantes de la ruta deben ser del Colegio San Cayetano.", await sinB.MensajeErrorAsync(HttpStatusCode.UnprocessableEntity));
        var repetido = await c.PutAsJsonAsync($"/api/v1/rutas/{ruta.Id}",
            DatosOperacion.Ruta(colegioA.Id, [a1.Id], colegioIds: [colegioA.Id, colegioA.Id]), ClienteApi.Json);
        Assert.Equal(HttpStatusCode.BadRequest, repetido.StatusCode);

        // El recorrido guarda el colegio de cada estudiante y lista los colegios en el orden de la recogida.
        var vehiculo = await c.CrearAsync<VehiculoDto>("vehiculos", DatosCatalogos.Vehiculo(DatosOperacion.Placa(), "1"));
        var usuario = ClienteApi.Unico("cond");
        var conductor = await c.CrearAsync<EmpleadoDto>("empleados", DatosCatalogos.Empleado(usuario));
        var asignacion = await c.CrearAsync<AsignacionDto>("asignaciones", DatosOperacion.Asignacion(ruta.Id, vehiculo.Id, conductor.Id, null));
        var cliente = await bd.ClienteDeAsync(usuario, "Clave-123");

        var delDia = Assert.Single(await (await cliente.GetAsync("/api/v1/recorridos/del-dia")).LeerAsync<List<RutaAsignadaDto>>());
        Assert.Equal("Colegio San Cayetano · Liceo Los Andes", delDia.Colegio);
        Assert.Equal(2, delDia.Colegios!.Count);

        var r = await (await cliente.IniciarAsync(asignacion.Id)).LeerAsync<RecorridoDto>(HttpStatusCode.Created);
        Assert.Equal([colegioA.Id.ToString(), colegioB.Id.ToString(), colegioA.Id.ToString()], r.Paradas.Select(p => p.ColegioId));
        Assert.Equal("Liceo Los Andes", r.Paradas[1].Colegio);
        Assert.Equal([colegioA.Id.ToString(), colegioB.Id.ToString()], r.Colegios!.Select(x => x.ColegioId));

        // Llegada al Liceo: se entregan sus estudiantes a bordo mientras sigue la recogida de los otros.
        await cliente.MarcarAsync(r.Id, "A_BORDO", a1.Id, b1.Id);
        var llegada = await cliente.MarcarAsync(r.Id, "ENTREGADO", b1.Id);
        Assert.Equal([EstadoParada.ABordo, EstadoParada.Entregado, EstadoParada.Pendiente], llegada.Paradas.Select(p => p.Estado));

        // La entrega recorre la ruta al revés.
        await cliente.MarcarAsync(r.Id, "A_BORDO", a2.Id);
        await cliente.MarcarAsync(r.Id, "ENTREGADO", a1.Id, a2.Id);
        await cliente.PostAsJsonAsync($"/api/v1/recorridos/{r.Id}/finalizar", new { observaciones = "" }, ClienteApi.Json);
        var entrega = await (await cliente.IniciarAsync(asignacion.Id, "ENTREGA")).LeerAsync<RecorridoDto>(HttpStatusCode.Created);
        Assert.Equal([a2.Id, b1.Id, a1.Id], entrega.Paradas.Select(p => p.EstudianteId));
    }

    [Fact]
    public async Task Si_el_administrador_cambia_el_orden_de_la_ruta_el_recorrido_en_curso_reordena_lo_que_falta()
    {
        var e = await bd.EscenarioAsync(estudiantes: 4);
        var (juan, valentina, samuel, isabella) = (e.Estudiantes[0].Id, e.Estudiantes[1].Id, e.Estudiantes[2].Id, e.Estudiantes[3].Id);
        var r = await (await e.ClienteConductor.IniciarAsync(e.Asignacion.Id)).LeerAsync<RecorridoDto>(HttpStatusCode.Created);
        await e.ClienteConductor.MarcarAsync(r.Id, "A_BORDO", juan);

        // Nuevo orden en la ruta: Isabella, Juan, Samuel, Valentina.
        await (await e.Admin.Cliente.PutAsJsonAsync($"/api/v1/rutas/{e.Ruta.Id}",
            DatosOperacion.Ruta(e.Colegio.Id, [isabella, juan, samuel, valentina]), ClienteApi.Json)).LeerAsync<RutaDto>();

        var actualizado = await (await e.ClienteMonitora!.GetAsync($"/api/v1/recorridos/{r.Id}")).LeerAsync<RecorridoDto>();
        // Juan ya va a bordo: queda primero; lo que falta sigue el nuevo orden.
        Assert.Equal([juan, isabella, samuel, valentina], actualizado.Paradas.Select(p => p.EstudianteId));
        Assert.Equal("", actualizado.OrdenActualizado!.EmpleadoId);
        Assert.EndsWith("(administrador)", actualizado.OrdenActualizado.Nombre);

        // Guardar la ruta sin cambiar el orden no toca el recorrido.
        var hora = actualizado.OrdenActualizado.Hora;
        await (await e.Admin.Cliente.PutAsJsonAsync($"/api/v1/rutas/{e.Ruta.Id}",
            DatosOperacion.Ruta(e.Colegio.Id, [isabella, juan, samuel, valentina]), ClienteApi.Json)).LeerAsync<RutaDto>();
        var igual = await (await e.ClienteMonitora!.GetAsync($"/api/v1/recorridos/{r.Id}")).LeerAsync<RecorridoDto>();
        Assert.Equal(hora, igual.OrdenActualizado!.Hora);
    }
}
