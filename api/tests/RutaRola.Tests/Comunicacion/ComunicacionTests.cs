using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using RutaRola.Api.Seguridad;
using RutaRola.Application.Comun;
using RutaRola.Application.Comunicacion;
using RutaRola.Application.Finanzas;
using RutaRola.Application.Operacion;
using RutaRola.Application.Personas;
using RutaRola.Domain.Enums;
using RutaRola.Tests.Infraestructura;

namespace RutaRola.Tests.Comunicacion;

[Collection(ColeccionBaseDatos.Nombre)]
public class ComunicacionTests(BaseDatosPrueba bd)
{
    /// <summary>Corre un servicio como el proceso del servidor para una empresa (solo su id_empresa).</summary>
    private Task<T> ComoProcesoAsync<T>(Guid empresa, Func<IServiceProvider, Task<T>> accion) => bd.EnScope(sp =>
    {
        sp.GetRequiredService<ContextoUsuarioHttp>().Usar(new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(Reclamos.IdEmpresa, empresa.ToString())], "ProcesoServidor")));
        return accion(sp);
    });

    private static async Task<ResumenNotificacionesDto> ResumenAsync(HttpClient c) =>
        await (await c.GetAsync("/api/v1/notificaciones/resumen")).LeerAsync<ResumenNotificacionesDto>();

    private static async Task<ListaPaginada<NotificacionDto>> ListarAsync(HttpClient c, string filtros = "") =>
        await (await c.GetAsync($"/api/v1/notificaciones?tamano=100{filtros}")).LeerAsync<ListaPaginada<NotificacionDto>>();

    [Fact]
    public async Task Genera_notificaciones_de_eventos_y_condiciones_una_sola_vez()
    {
        var admin = await bd.AdminNuevoAsync();
        var c = admin.Cliente;
        var (_, acudiente, estudiante) = await c.CrearEstudianteAsync();
        var factura = await c.CrearAsync<FacturaDto>("facturas", DatosFinanzas.Factura(acudiente.Id, estudiante.Id, fin: DatosFinanzas.Periodo(1)));
        var recibo = await c.CrearAsync<ReciboDto>("recibos", DatosFinanzas.Recibo(factura.Id, DatosFinanzas.Periodo(-3)));

        var resumen = await ResumenAsync(c);
        var todas = (await ListarAsync(c)).Items;
        var claves = todas.Select(n => n.Clave).ToList();
        Assert.Contains($"factura:{factura.Id}", claves);
        Assert.Contains($"recibo:{recibo.Id}", claves);
        Assert.Contains($"factura-termina:{factura.Id}:{DatosFinanzas.Periodo(1)}", claves);
        var mora = Assert.Single(todas, n => n.Clave.StartsWith($"mora:{acudiente.Id}:"));
        Assert.Equal((NivelNotificacion.Critica, CategoriaNotificacion.Pagos, "/finanzas/reportes-financieros/morosos"), (mora.Nivel, mora.Categoria, mora.Enlace));
        Assert.StartsWith("María Pérez López debe", mora.Mensaje);
        Assert.Equal(todas.Count, resumen.NoLeidas);
        Assert.Equal(1, resumen.Criticas);
        Assert.Equal(Math.Min(6, todas.Count), resumen.Recientes.Count);
        var pago = Assert.Single(todas, n => n.Clave == $"recibo:{recibo.Id}");
        Assert.Equal($"Pago recibido · {recibo.Numero}", pago.Titulo);
        Assert.Contains("$ 420.000", pago.Mensaje);

        // Leer, archivar (también la marca leída), filtrar y restaurar.
        await (await c.PatchAsJsonAsync("/api/v1/notificaciones/lectura", new { ids = new[] { pago.Id }, leida = true }, ClienteApi.Json)).LeerAsync(HttpStatusCode.NoContent);
        Assert.Equal(todas.Count - 1, (await ResumenAsync(c)).NoLeidas);
        Assert.Equal([pago.Id], (await ListarAsync(c, "&lectura=LEIDAS")).Items.Select(n => n.Id));
        await (await c.PatchAsJsonAsync("/api/v1/notificaciones/archivo", new { ids = new[] { mora.Id }, archivada = true }, ClienteApi.Json)).LeerAsync(HttpStatusCode.NoContent);
        var archivada = Assert.Single((await ListarAsync(c, "&bandeja=ARCHIVADAS")).Items);
        Assert.True(archivada.Leida && archivada.Archivada);
        Assert.Equal(0, (await ResumenAsync(c)).Criticas);
        Assert.Single((await ListarAsync(c, "&categoria=FACTURACION&texto=termina")).Items);

        // Eliminar no hace que el mismo hecho vuelva: una escritura nueva solo agrega lo nuevo.
        await (await c.PostAsJsonAsync("/api/v1/notificaciones/eliminar", new { ids = new[] { pago.Id } }, ClienteApi.Json)).LeerAsync(HttpStatusCode.NoContent);
        await (await c.DeleteAsync("/api/v1/notificaciones?bandeja=ARCHIVADAS")).LeerAsync(HttpStatusCode.NoContent);
        var cotizacion = await c.CrearAsync<CotizacionDto>("cotizaciones", new
        {
            fecha = DatosOperacion.Hoy, registrados = false, acudienteId = "", nombreAcudiente = "Camila Torres", telefonoAcudiente = "3209988776",
            estudiantes = new[] { new { estudianteId = (Guid?)null, nombre = "Lucía Torres" } }, valorPorEstudiante = 380000, estado = "PENDIENTE",
            observaciones = "",
        });
        await ResumenAsync(c);
        var despues = (await ListarAsync(c)).Items.Select(n => n.Clave).ToList();
        Assert.Contains($"cotizacion:{cotizacion.Id}", despues);
        Assert.DoesNotContain($"recibo:{recibo.Id}", despues);
        Assert.Empty((await ListarAsync(c, "&bandeja=ARCHIVADAS")).Items);
        Assert.DoesNotContain(despues, k => k.StartsWith("mora:"));

        await (await c.PostAsync("/api/v1/notificaciones/leer-todas", null)).LeerAsync(HttpStatusCode.NoContent);
        Assert.Equal(0, (await ResumenAsync(c)).NoLeidas);
    }

    [Fact]
    public async Task Cada_usuario_solo_ve_y_modifica_sus_notificaciones()
    {
        var a = await bd.AdminNuevoAsync();
        var (_, acudiente, estudiante) = await a.Cliente.CrearEstudianteAsync();
        await a.Cliente.CrearAsync<FacturaDto>("facturas", DatosFinanzas.Factura(acudiente.Id, estudiante.Id));
        await ResumenAsync(a.Cliente);
        var deA = (await ListarAsync(a.Cliente)).Items;
        Assert.NotEmpty(deA);

        var b = await bd.AdminNuevoAsync();
        Assert.Equal(0, (await ResumenAsync(b.Cliente)).NoLeidas);
        Assert.Empty((await ListarAsync(b.Cliente)).Items);
        var ids = deA.Select(n => n.Id).ToArray();
        await (await b.Cliente.PatchAsJsonAsync("/api/v1/notificaciones/lectura", new { ids, leida = true }, ClienteApi.Json)).LeerAsync(HttpStatusCode.NoContent);
        await (await b.Cliente.PostAsJsonAsync("/api/v1/notificaciones/eliminar", new { ids }, ClienteApi.Json)).LeerAsync(HttpStatusCode.NoContent);
        await (await b.Cliente.DeleteAsync("/api/v1/notificaciones?bandeja=ENTRADA")).LeerAsync(HttpStatusCode.NoContent);
        Assert.Equal(deA.Count, (await ResumenAsync(a.Cliente)).NoLeidas);

        // El acudiente tiene bandeja propia (vacía: las reglas son de administración) y no ve las del administrador.
        var cliente = await bd.ClienteDeAsync(acudiente.Usuario, "Clave-123");
        Assert.Equal(0, (await ResumenAsync(cliente)).NoLeidas);
        Assert.Equal(HttpStatusCode.Unauthorized, (await bd.Api.CreateClient().GetAsync("/api/v1/notificaciones/resumen")).StatusCode);
    }

    [Fact]
    public async Task El_proceso_de_cobro_envia_un_recordatorio_por_canal_y_mes()
    {
        var admin = await bd.AdminNuevoAsync();
        var c = admin.Cliente;
        var (colegio, acudiente, estudiante) = await c.CrearEstudianteAsync();
        var factura = await c.CrearAsync<FacturaDto>("facturas", DatosFinanzas.Factura(acudiente.Id, estudiante.Id));
        // Día límite = hoy: el recordatorio está dentro de la ventana (límite − días previos … límite).
        var programacion = await c.CrearAsync<ProgramacionDto>("programaciones", DatosFinanzas.Programacion(factura.Id,
            whatsapp: [DatosFinanzas.Plantilla("EN_USO", "Hola {acudiente}, {estudiante} debe {mes} por {valor} ({factura}) hasta el {fecha_limite}.")],
            correos: [DatosFinanzas.Plantilla("EN_USO", "Cuerpo {valor}", "Recordatorio {mes}", [DatosFinanzas.Adjunto()])], conEmail: true,
            dia: DatosOperacion.Hoy.Day));

        // Otra factura del mismo mes ya pagada: no se le recuerda nada.
        var otro = await c.CrearAsync<EstudianteDto>("estudiantes", DatosCatalogos.Estudiante(acudiente.Id, colegio.Id, primerNombre: "Valentina"));
        var pagada = await c.CrearAsync<FacturaDto>("facturas", DatosFinanzas.Factura(acudiente.Id, otro.Id));
        await c.CrearAsync<ProgramacionDto>("programaciones", DatosFinanzas.Programacion(pagada.Id, dia: DatosOperacion.Hoy.Day));
        await c.CrearAsync<ReciboDto>("recibos", DatosFinanzas.Recibo(pagada.Id, DatosFinanzas.Periodo()));

        Assert.Equal(2, await ComoProcesoAsync(admin.Empresa.Id, sp => sp.GetRequiredService<ServicioCobros>().EnviarRecordatoriosAsync()));
        Assert.Equal(0, await ComoProcesoAsync(admin.Empresa.Id, sp => sp.GetRequiredService<ServicioCobros>().EnviarRecordatoriosAsync()));

        var envios = await (await c.GetAsync($"/api/v1/programaciones/{programacion.Id}/envios")).LeerAsync<List<EnvioDto>>();
        Assert.Equal([(CanalEnvio.Whatsapp, "3001234567"), (CanalEnvio.Email, "maria.perez@correo.com")],
            envios.Select(e => (e.Canal, e.Destinatario)).Order());
        Assert.All(envios, e => Assert.Equal((DatosFinanzas.Periodo(), EstadoEnvio.Enviado), (e.Periodo, e.Estado)));

        var mensajes = await (await c.GetAsync("/api/v1/mensajes-whatsapp")).LeerAsync<ListaPaginada<MensajeWhatsappDto>>();
        var mensaje = Assert.Single(mensajes.Items);
        Assert.Equal((EstadoMensaje.Enviado, "3001234567", estudiante.Id), (mensaje.Estado, mensaje.Telefono, mensaje.EstudianteId));
        Assert.StartsWith($"Hola María Pérez López, Juan Sebastián Pérez López debe {Periodos.Nombre(Periodos.De(DatosOperacion.Hoy), true)} por $ 420.000 ({factura.Numero}) hasta el {Fechas.Corta(DatosOperacion.Hoy)}.", mensaje.Mensaje);

        // El envío queda en la campana; otra empresa no ve el registro de WhatsApp.
        await ResumenAsync(c);
        Assert.Contains((await ListarAsync(c, "&categoria=ENVIOS")).Items, n => n.Titulo == "Recordatorio enviado por WhatsApp");
        var otra = await bd.AdminNuevoAsync();
        Assert.Empty((await (await otra.Cliente.GetAsync("/api/v1/mensajes-whatsapp")).LeerAsync<ListaPaginada<MensajeWhatsappDto>>()).Items);
    }

    [Fact]
    public async Task Sin_correo_del_acudiente_el_envio_queda_fallido_y_se_notifica()
    {
        var admin = await bd.AdminNuevoAsync();
        var c = admin.Cliente;
        var colegio = await c.CrearColegioAsync();
        var datos = DatosCatalogos.Acudiente(ClienteApi.Unico("acu"));
        datos["correo"] = "";
        var acudiente = await c.CrearAsync<AcudienteDto>("acudientes", datos);
        var estudiante = await c.CrearAsync<EstudianteDto>("estudiantes", DatosCatalogos.Estudiante(acudiente.Id, colegio.Id));
        var factura = await c.CrearAsync<FacturaDto>("facturas", DatosFinanzas.Factura(acudiente.Id, estudiante.Id));
        var programacion = await c.CrearAsync<ProgramacionDto>("programaciones", DatosFinanzas.Programacion(factura.Id, conWhatsapp: false,
            correos: [DatosFinanzas.Plantilla("EN_USO", asunto: "Cobro")], conEmail: true, dia: DatosOperacion.Hoy.Day));

        Assert.Equal(1, await ComoProcesoAsync(admin.Empresa.Id, sp => sp.GetRequiredService<ServicioCobros>().EnviarRecordatoriosAsync()));
        var envio = Assert.Single(await (await c.GetAsync($"/api/v1/programaciones/{programacion.Id}/envios")).LeerAsync<List<EnvioDto>>());
        Assert.Equal((EstadoEnvio.Fallido, "El acudiente no tiene correo registrado."), (envio.Estado, envio.Detalle));

        Assert.True(await ComoProcesoAsync(admin.Empresa.Id, sp => sp.GetRequiredService<GeneradorNotificaciones>().GenerarAsync()) > 0);
        var fallo = Assert.Single((await ListarAsync(c, "&categoria=ENVIOS")).Items);
        Assert.Equal(("Falló un envío por correo", NivelNotificacion.Critica), (fallo.Titulo, fallo.Nivel));
    }

    [Fact]
    public async Task El_portal_muestra_solo_lo_del_acudiente_del_token()
    {
        var e = await bd.EscenarioAsync(estudiantes: 2);
        var c = e.Admin.Cliente;
        var juan = e.Estudiantes[0];
        var factura = await c.CrearAsync<FacturaDto>("facturas", DatosFinanzas.Factura(e.Acudiente.Id, juan.Id));
        await c.CrearAsync<ProgramacionDto>("programaciones", DatosFinanzas.Programacion(factura.Id, dia: 31));
        var recibo = await c.CrearAsync<ReciboDto>("recibos", DatosFinanzas.Recibo(factura.Id, DatosFinanzas.Periodo(-3), medio: "EFECTIVO"));
        await c.CrearAsync<CotizacionDto>("cotizaciones", new
        {
            fecha = DatosOperacion.Hoy, registrados = true, acudienteId = e.Acudiente.Id, nombreAcudiente = "", telefonoAcudiente = "",
            estudiantes = new[] { new { estudianteId = (Guid?)juan.Id, nombre = "Juan" } }, valorPorEstudiante = 400000, estado = "APROBADA", observaciones = "",
        });
        // El conductor inicia la recogida y sube a Juan.
        var recorrido = await (await e.ClienteConductor.IniciarAsync(e.Asignacion.Id)).LeerAsync<RecorridoDto>(HttpStatusCode.Created);
        await e.ClienteConductor.MarcarAsync(recorrido.Id, "A_BORDO", juan.Id);

        // Otro acudiente de la misma empresa con su propio estudiante.
        var (_, otroAcudiente, otroEstudiante) = await c.CrearEstudianteAsync();
        await c.CrearAsync<FacturaDto>("facturas", DatosFinanzas.Factura(otroAcudiente.Id, otroEstudiante.Id));

        var cliente = await bd.ClienteDeAsync(e.Acudiente.Usuario, "Clave-123");
        var portal = await (await cliente.GetAsync("/api/v1/portal")).LeerAsync<PortalAcudienteDto>();
        Assert.Equal("María Pérez López", portal.Acudiente.Nombre);
        Assert.Equal(e.Estudiantes.Select(x => x.Id).Order(), portal.Estudiantes.Select(x => x.Id).Order());
        var j = Assert.Single(portal.Estudiantes, x => x.Id == juan.Id);
        Assert.Equal(("1° · Primero", "JP"), (j.Grado, j.Iniciales));
        Assert.NotNull(j.Horario);
        Assert.Equal(e.Vehiculo.Placa, j.Transporte?.Vehiculo);
        Assert.Equal(e.Conductor.Telefono, j.Transporte?.TelefonoConductor);
        var hoy = Assert.Single(j.Hoy);
        Assert.Equal((Trayecto.Recogida, true, EstadoParada.ABordo), (hoy.Trayecto, hoy.EnCurso, hoy.Estado));
        Assert.NotNull(hoy.HoraAbordo);

        var f = Assert.Single(portal.Facturas);
        Assert.Equal(factura.Id, f.Id);
        Assert.Equal(7, f.Cuotas.Count);
        Assert.Equal(
            [EstadoCuota.Pagada, EstadoCuota.Vencida, EstadoCuota.Vencida, EstadoCuota.Pendiente, EstadoCuota.Programada, EstadoCuota.Programada, EstadoCuota.Programada],
            f.Cuotas.Select(x => x.Estado));
        Assert.Equal(recibo.Numero, f.Cuotas[0].Recibo?.Numero);
        Assert.Equal((420000m, 840000m), (f.Pagado, f.Vencido));
        Assert.Equal((840000m, 2, 420000m), (portal.Resumen.Vencido, portal.Resumen.CuotasVencidas, portal.Resumen.PorPagar));
        Assert.Equal((EstadoCuota.Vencida, DatosFinanzas.Periodo(-2)), (portal.Resumen.ProximoPago?.Estado, portal.Resumen.ProximoPago?.Periodo));
        Assert.Equal(recibo.Id, Assert.Single(portal.Recibos).Id);
        Assert.Equal(EstadoCotizacion.Aprobada, Assert.Single(portal.Cotizaciones).Estado);

        // Solo el acudiente usa el portal; el acudiente no consulta los módulos de administración.
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/v1/portal")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await e.ClienteConductor.GetAsync("/api/v1/portal")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync("/api/v1/estudiantes")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync("/api/v1/mensajes-whatsapp")).StatusCode);

        var otro = await bd.ClienteDeAsync(otroAcudiente.Usuario, "Clave-123");
        var suyo = await (await otro.GetAsync("/api/v1/portal")).LeerAsync<PortalAcudienteDto>();
        Assert.Equal([otroEstudiante.Id], suyo.Estudiantes.Select(x => x.Id));
        Assert.Null(suyo.Estudiantes[0].Transporte);
        Assert.Empty(suyo.Cotizaciones);
        Assert.Empty(suyo.Recibos);
    }
}
