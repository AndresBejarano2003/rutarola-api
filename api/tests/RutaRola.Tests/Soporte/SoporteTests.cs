using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RutaRola.Application.Comun;
using RutaRola.Application.Comunicacion;
using RutaRola.Application.Soporte;
using RutaRola.Domain.Enums;
using RutaRola.Tests.Infraestructura;

namespace RutaRola.Tests.Soporte;

/// <summary>Tickets de soporte: la empresa los abre y sigue; la plataforma los lleva hasta el cierre.</summary>
[Collection(ColeccionBaseDatos.Nombre)]
public class SoporteTests(BaseDatosPrueba bd)
{
    private const string Empresa = "/api/v1/soporte/tickets";
    private const string Plataforma = "/api/v1/plataforma/tickets";
    private static readonly string Pdf = "data:application/pdf;base64," + Convert.ToBase64String("%PDF-1.4 prueba"u8.ToArray());

    private static object Ticket(string tipo = "ERROR", string prioridad = "MEDIA", object[]? adjuntos = null) => new
    {
        tipo, modulo = "Finanzas", prioridad, asunto = "No carga la factura",
        descripcion = "Al abrir la factura FAC-0001 la pantalla queda en blanco.", adjuntos = adjuntos ?? [],
    };

    private static async Task<TicketDto> EstadoAsync(HttpClient super, Guid id, string estado, string? comentario = null) =>
        await (await super.PostAsJsonAsync($"{Plataforma}/{id}/estado", new { estado, comentario }, ClienteApi.Json)).LeerAsync<TicketDto>();

    [Fact]
    public async Task Un_error_recorre_revision_proceso_qa_pruebas_y_validacion_hasta_cerrarse()
    {
        var admin = await bd.AdminNuevoAsync();
        var super = await bd.SuperAdminAsync();

        var creado = await (await admin.Cliente.PostAsJsonAsync(Empresa,
            Ticket(adjuntos: [new { nombre = "captura.pdf", tipo = "application/pdf", url = Pdf }]), ClienteApi.Json)).LeerAsync<TicketDto>(HttpStatusCode.Created);
        Assert.StartsWith("SOP-", creado.Numero);
        Assert.Equal((EstadoTicket.Nuevo, "captura.pdf"), (creado.Estado, Assert.Single(creado.Adjuntos).Nombre));
        Assert.True(creado.Acciones.Cancelar);
        Assert.Empty(creado.SiguientesEstados);

        // La plataforma lo ve en su bandeja y en el resumen.
        var bandeja = await (await super.GetAsync($"{Plataforma}?estado=ABIERTOS&empresaId={admin.Empresa.Id}")).LeerAsync<ListaPaginada<TicketResumenDto>>();
        Assert.Equal(creado.Id, Assert.Single(bandeja.Items).Id);
        Assert.True((await (await super.GetAsync($"{Plataforma}/resumen")).LeerAsync<ResumenTicketsDto>()).PorEstado["NUEVO"] >= 1);

        // Un error no se resuelve sin QA, pruebas y validación; los cambios hacia atrás piden comentario.
        var t = await EstadoAsync(super, creado.Id, "EN_REVISION");
        Assert.DoesNotContain(EstadoTicket.Resuelto, t.SiguientesEstados);
        Assert.Contains("QA", await (await super.PostAsJsonAsync($"{Plataforma}/{creado.Id}/estado", new { estado = "RESUELTO", comentario = "Listo" }, ClienteApi.Json))
            .MensajeErrorAsync(HttpStatusCode.UnprocessableEntity));
        await EstadoAsync(super, creado.Id, "EN_PROCESO");
        await EstadoAsync(super, creado.Id, "EN_QA");
        Assert.Equal("Explica el motivo del cambio: la empresa lo verá en el historial.", await (await super.PostAsJsonAsync(
            $"{Plataforma}/{creado.Id}/estado", new { estado = "EN_PROCESO" }, ClienteApi.Json)).MensajeErrorAsync(HttpStatusCode.UnprocessableEntity));
        await EstadoAsync(super, creado.Id, "EN_PRUEBAS");
        t = await EstadoAsync(super, creado.Id, "VALIDACION_CLIENTE");
        Assert.NotNull(t.FechaPrimeraRespuesta);

        // La empresa rechaza la solución: vuelve a proceso. Luego la aprueba y la confirma con calificación.
        var vista = await (await admin.Cliente.GetAsync($"{Empresa}/{creado.Id}")).LeerAsync<TicketDto>();
        Assert.True(vista.Acciones.Validar);
        await (await admin.Cliente.PostAsJsonAsync($"{Empresa}/{creado.Id}/validacion", new { aprobada = false, comentario = "Sigue en blanco" }, ClienteApi.Json)).LeerAsync<TicketDto>();
        Assert.Equal(EstadoTicket.EnProceso, (await (await super.GetAsync($"{Plataforma}/{creado.Id}")).LeerAsync<TicketDto>()).Estado);
        foreach (var e in new[] { "EN_QA", "EN_PRUEBAS", "VALIDACION_CLIENTE" }) await EstadoAsync(super, creado.Id, e);
        var resuelto = await (await admin.Cliente.PostAsJsonAsync($"{Empresa}/{creado.Id}/validacion", new { aprobada = true }, ClienteApi.Json)).LeerAsync<TicketDto>();
        Assert.Equal(EstadoTicket.Resuelto, resuelto.Estado);
        Assert.True(resuelto.Acciones.Confirmar && resuelto.Acciones.Reabrir && !resuelto.Acciones.Cancelar);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Cliente.PostAsJsonAsync($"{Empresa}/{creado.Id}/confirmacion", new { calificacion = 6 }, ClienteApi.Json)).StatusCode);
        var cerrado = await (await admin.Cliente.PostAsJsonAsync($"{Empresa}/{creado.Id}/confirmacion", new { calificacion = 5, comentario = "Gracias" }, ClienteApi.Json)).LeerAsync<TicketDto>();
        Assert.Equal((EstadoTicket.Cerrado, 5), (cerrado.Estado, cerrado.Calificacion));
        Assert.NotNull(cerrado.FechaCierre);
        Assert.False(cerrado.Acciones.Comentar);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.Cliente.PostAsJsonAsync($"{Empresa}/{creado.Id}/comentarios", new { texto = "Otra cosa" }, ClienteApi.Json)).StatusCode);

        // El solicitante recibió avisos en la campana por cada avance de la plataforma.
        var campana = await (await admin.Cliente.GetAsync("/api/v1/notificaciones?tamano=100")).LeerAsync<ListaPaginada<NotificacionDto>>();
        Assert.Contains(campana.Items, n => n.Titulo.StartsWith(creado.Numero) && n.Enlace == $"/sistema/soporte?ticket={creado.Id}");
    }

    [Fact]
    public async Task Las_notas_internas_no_llegan_a_la_empresa_y_responder_lo_devuelve_a_revision()
    {
        var admin = await bd.AdminNuevoAsync();
        var super = await bd.SuperAdminAsync();
        var creado = await (await admin.Cliente.PostAsJsonAsync(Empresa, Ticket("CONSULTA", "BAJA"), ClienteApi.Json)).LeerAsync<TicketDto>(HttpStatusCode.Created);

        await (await super.PostAsJsonAsync($"{Plataforma}/{creado.Id}/comentarios", new
        {
            texto = "Revisar logs del 5", interno = true,
            adjuntos = new[] { new { nombre = "log.pdf", tipo = "application/pdf", url = Pdf } },
        }, ClienteApi.Json)).LeerAsync<TicketDto>();
        await EstadoAsync(super, creado.Id, "EN_REVISION");
        await EstadoAsync(super, creado.Id, "ESPERANDO_CLIENTE", "¿Desde qué navegador?");

        var interno = (await (await super.GetAsync($"{Plataforma}/{creado.Id}")).LeerAsync<TicketDto>()).Eventos.Single(e => e.Interno);
        var vista = await (await admin.Cliente.GetAsync($"{Empresa}/{creado.Id}")).LeerAsync<TicketDto>();
        Assert.DoesNotContain(vista.Eventos, e => e.Interno || e.Texto.Contains("logs"));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Cliente.GetAsync($"{Empresa}/{creado.Id}/adjuntos/{interno.Adjuntos[0].Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await super.GetAsync($"{Plataforma}/{creado.Id}/adjuntos/{interno.Adjuntos[0].Id}")).StatusCode);

        // La empresa no puede marcar su comentario como interno; responder lo devuelve a revisión.
        var respondido = await (await admin.Cliente.PostAsJsonAsync($"{Empresa}/{creado.Id}/comentarios", new { texto = "Chrome", interno = true }, ClienteApi.Json)).LeerAsync<TicketDto>();
        Assert.Equal(EstadoTicket.EnRevision, respondido.Estado);
        Assert.Contains(respondido.Eventos, e => e.Texto == "Chrome" && !e.Interno);

        // Una consulta sí se resuelve desde la revisión.
        Assert.Equal(EstadoTicket.Resuelto, (await EstadoAsync(super, creado.Id, "RESUELTO", "Usa Chrome 120 o superior.")).Estado);
        var reabierto = await (await admin.Cliente.PostAsJsonAsync($"{Empresa}/{creado.Id}/reapertura", new { comentario = "Sigue fallando" }, ClienteApi.Json)).LeerAsync<TicketDto>();
        Assert.Equal(EstadoTicket.EnRevision, reabierto.Estado);
        var cancelado = await (await admin.Cliente.PostAsJsonAsync($"{Empresa}/{creado.Id}/cancelacion", new { comentario = "Ya funcionó" }, ClienteApi.Json)).LeerAsync<TicketDto>();
        Assert.Equal(EstadoTicket.Cancelado, cancelado.Estado);
        Assert.Empty((await (await super.GetAsync($"{Plataforma}/{creado.Id}")).LeerAsync<TicketDto>()).SiguientesEstados);
    }

    [Fact]
    public async Task Asigna_responsable_y_prioridad_y_agenda_y_registra_reuniones()
    {
        var admin = await bd.AdminNuevoAsync();
        var super = await bd.SuperAdminAsync();
        var creado = await (await admin.Cliente.PostAsJsonAsync(Empresa, Ticket("CAPACITACION", "BAJA"), ClienteApi.Json)).LeerAsync<TicketDto>(HttpStatusCode.Created);
        Assert.InRange(creado.FechaObjetivo - creado.FechaCreacion, TimeSpan.FromDays(7) - TimeSpan.FromSeconds(2), TimeSpan.FromDays(7));

        var responsable = (await (await super.GetAsync($"{Plataforma}/responsables")).LeerAsync<List<ResponsableDto>>()).First();
        var asignado = await (await super.PutAsJsonAsync($"{Plataforma}/{creado.Id}/asignacion",
            new { responsableId = responsable.Id.ToString(), prioridad = "ALTA" }, ClienteApi.Json)).LeerAsync<TicketDto>();
        Assert.Equal((responsable.Id.ToString(), PrioridadTicket.Alta), (asignado.ResponsableId, asignado.Prioridad));
        Assert.Equal(TimeSpan.FromHours(24), asignado.FechaObjetivo - asignado.FechaCreacion);
        Assert.Equal(HttpStatusCode.NotFound, (await super.PutAsJsonAsync($"{Plataforma}/{creado.Id}/asignacion",
            new { responsableId = Guid.NewGuid().ToString(), prioridad = "ALTA" }, ClienteApi.Json)).StatusCode);
        var porResponsable = await (await super.GetAsync($"{Plataforma}?responsableId={responsable.Id}&empresaId={admin.Empresa.Id}")).LeerAsync<ListaPaginada<TicketResumenDto>>();
        Assert.Single(porResponsable.Items);

        // Una reunión virtual necesita enlace; luego se registra como realizada con acta.
        var fecha = DateTime.UtcNow.AddDays(2);
        Assert.Equal(HttpStatusCode.BadRequest, (await super.PostAsJsonAsync($"{Plataforma}/{creado.Id}/reuniones",
            new { fecha, duracionMinutos = 30, modalidad = "VIRTUAL", motivo = "Capacitación en facturas" }, ClienteApi.Json)).StatusCode);
        var conReunion = await (await super.PostAsJsonAsync($"{Plataforma}/{creado.Id}/reuniones",
            new { fecha, duracionMinutos = 30, modalidad = "VIRTUAL", enlace = "https://meet.example.com/abc", motivo = "Capacitación en facturas" }, ClienteApi.Json))
            .LeerAsync<TicketDto>();
        var reunion = Assert.Single(conReunion.Reuniones);
        Assert.Equal(EstadoReunion.Programada, reunion.Estado);
        var lista = await (await super.GetAsync($"{Plataforma}?empresaId={admin.Empresa.Id}")).LeerAsync<ListaPaginada<TicketResumenDto>>();
        Assert.NotNull(Assert.Single(lista.Items).ProximaReunion);

        var realizada = await (await super.PutAsJsonAsync($"{Plataforma}/{creado.Id}/reuniones/{reunion.Id}",
            new { estado = "REALIZADA", notas = "Se explicó cómo anular y reemplazar una factura." }, ClienteApi.Json)).LeerAsync<TicketDto>();
        Assert.Equal(EstadoReunion.Realizada, realizada.Reuniones[0].Estado);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await super.PutAsJsonAsync($"{Plataforma}/{creado.Id}/reuniones/{reunion.Id}",
            new { estado = "CANCELADA", notas = "Otra vez" }, ClienteApi.Json)).StatusCode);

        // La empresa ve la reunión y el acta en su historial.
        var vista = await (await admin.Cliente.GetAsync($"{Empresa}/{creado.Id}")).LeerAsync<TicketDto>();
        Assert.Single(vista.Reuniones);
        Assert.Contains(vista.Eventos, e => e.Tipo == TipoEventoTicket.Reunion && e.Texto.Contains("anular"));
    }

    [Fact]
    public async Task Cada_empresa_ve_solo_sus_tickets_y_la_bandeja_es_solo_de_la_plataforma()
    {
        var a = await bd.AdminNuevoAsync();
        var b = await bd.AdminNuevoAsync();
        var ticketA = await (await a.Cliente.PostAsJsonAsync(Empresa, Ticket(), ClienteApi.Json)).LeerAsync<TicketDto>(HttpStatusCode.Created);

        Assert.Empty((await (await b.Cliente.GetAsync(Empresa)).LeerAsync<ListaPaginada<TicketResumenDto>>()).Items);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Cliente.GetAsync($"{Empresa}/{ticketA.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Cliente.PostAsJsonAsync($"{Empresa}/{ticketA.Id}/comentarios", new { texto = "Hola" }, ClienteApi.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Cliente.PostAsJsonAsync($"{Empresa}/{ticketA.Id}/cancelacion", new { comentario = "No" }, ClienteApi.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await a.Cliente.GetAsync(Plataforma)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await a.Cliente.PostAsJsonAsync($"{Plataforma}/{ticketA.Id}/estado", new { estado = "EN_REVISION" }, ClienteApi.Json)).StatusCode);

        // Validaciones de la solicitud: la empresa no elige "Crítica", textos con longitud y adjuntos ≤ 1 MB.
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Cliente.PostAsJsonAsync(Empresa, Ticket(prioridad: "CRITICA"), ClienteApi.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Cliente.PostAsJsonAsync(Empresa,
            new { tipo = "ERROR", modulo = "Finanzas", prioridad = "MEDIA", asunto = "Hola", descripcion = "corta", adjuntos = Array.Empty<object>() }, ClienteApi.Json)).StatusCode);
        var grande = "data:application/pdf;base64," + Convert.ToBase64String(new byte[ReglasTicket.TamanoMaximoAdjunto + 1]);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await a.Cliente.PostAsJsonAsync(Empresa,
            Ticket(adjuntos: [new { nombre = "grande.pdf", tipo = "application/pdf", url = grande }]), ClienteApi.Json)).StatusCode);
    }

    [Fact]
    public async Task El_proceso_cierra_los_resueltos_sin_respuesta_despues_de_siete_dias()
    {
        var admin = await bd.AdminNuevoAsync();
        var super = await bd.SuperAdminAsync();
        var viejo = await (await admin.Cliente.PostAsJsonAsync(Empresa, Ticket("CONSULTA"), ClienteApi.Json)).LeerAsync<TicketDto>(HttpStatusCode.Created);
        var reciente = await (await admin.Cliente.PostAsJsonAsync(Empresa, Ticket("CONSULTA"), ClienteApi.Json)).LeerAsync<TicketDto>(HttpStatusCode.Created);
        foreach (var id in new[] { viejo.Id, reciente.Id })
        {
            await EstadoAsync(super, id, "EN_REVISION");
            await EstadoAsync(super, id, "RESUELTO", "Respondido por correo.");
        }
        await using (var db = bd.Contexto(null, modoSistema: true))
        {
            await db.TicketsSoporte.Where(t => t.Id == viejo.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.FechaResolucion, DateTime.UtcNow.AddDays(-ReglasTicket.DiasCierreAutomatico - 1)));
        }

        Assert.True(await bd.EnScope(sp => sp.GetRequiredService<ServicioSoporte>().CerrarResueltosAsync()) >= 1);
        Assert.Equal(EstadoTicket.Cerrado, (await (await admin.Cliente.GetAsync($"{Empresa}/{viejo.Id}")).LeerAsync<TicketDto>()).Estado);
        Assert.Equal(EstadoTicket.Resuelto, (await (await admin.Cliente.GetAsync($"{Empresa}/{reciente.Id}")).LeerAsync<TicketDto>()).Estado);
    }
}
