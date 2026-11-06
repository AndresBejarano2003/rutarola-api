using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Application.Finanzas;
using RutaRola.Domain.Enums;
using RutaRola.Domain.Finanzas;
using RutaRola.Tests.Infraestructura;

namespace RutaRola.Tests.Finanzas;

[Collection(ColeccionBaseDatos.Nombre)]
public class CotizacionesProgramacionesTests(BaseDatosPrueba bd)
{
    private static Dictionary<string, object?> Cotizacion(bool registrados, Guid? acudienteId, object[] estudiantes, decimal valor = 400000) => new()
    {
        ["numero"] = "",
        ["fecha"] = DatosOperacion.Hoy.ToString("yyyy-MM-dd"),
        ["registrados"] = registrados,
        ["acudienteId"] = acudienteId,
        ["nombreAcudiente"] = registrados ? "" : "Camila Torres Ramírez",
        ["telefonoAcudiente"] = registrados ? "" : "3209988776",
        ["estudiantes"] = estudiantes,
        ["valorPorEstudiante"] = valor,
        ["valorTotal"] = 1,
        ["estado"] = "PENDIENTE",
        ["observaciones"] = "",
    };

    [Fact]
    public async Task Cotiza_a_registrados_y_a_clientes_nuevos()
    {
        var admin = await bd.AdminNuevoAsync();
        var c = admin.Cliente;
        var (_, acudiente, estudiante) = await c.CrearEstudianteAsync();

        var nueva = await c.CrearAsync<CotizacionDto>("cotizaciones",
            Cotizacion(false, null, [new { estudianteId = (Guid?)null, nombre = "Miguel Ángel Torres" }, new { estudianteId = (Guid?)null, nombre = "Lucía Torres" }], 380000));
        Assert.Equal(("COT-0001", 760000m, false), (nueva.Numero, nueva.ValorTotal, nueva.Registrados));
        var json = JsonDocument.Parse(await (await c.GetAsync($"/api/v1/cotizaciones/{nueva.Id}")).Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(JsonValueKind.Null, json.GetProperty("acudienteId").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("estudiantes")[0].GetProperty("estudianteId").ValueKind);
        Assert.Equal("PENDIENTE", json.GetProperty("estado").GetString());

        var registrada = await c.CrearAsync<CotizacionDto>("cotizaciones",
            Cotizacion(true, acudiente.Id, [new { estudianteId = (Guid?)estudiante.Id, nombre = "otro nombre" }]));
        Assert.Equal(("COT-0002", "María Pérez López", "3001234567"), (registrada.Numero, registrada.NombreAcudiente, registrada.TelefonoAcudiente));
        Assert.Equal("Juan Sebastián Pérez López", Assert.Single(registrada.Estudiantes).Nombre);

        var sinEstudiantes = await c.PostAsJsonAsync("/api/v1/cotizaciones", Cotizacion(false, null, []), ClienteApi.Json);
        Assert.Equal("Agrega al menos un estudiante a la cotización.", await sinEstudiantes.MensajeErrorAsync(HttpStatusCode.BadRequest));
        var sinAcudiente = await c.PostAsJsonAsync("/api/v1/cotizaciones", Cotizacion(true, null, [new { estudianteId = (Guid?)null, nombre = "X" }]), ClienteApi.Json);
        Assert.Equal("Elige el acudiente registrado.", await sinAcudiente.MensajeErrorAsync(HttpStatusCode.BadRequest));

        Assert.Equal([nueva.Id], (await (await c.GetAsync("/api/v1/cotizaciones?texto=lucia")).LeerAsync<ListaPaginada<CotizacionDto>>()).Items.Select(x => x.Id));
        Assert.Equal(2, (await (await c.GetAsync($"/api/v1/cotizaciones?estado=PENDIENTE&desde={DatosOperacion.Hoy:yyyy-MM-dd}")).LeerAsync<ListaPaginada<CotizacionDto>>()).Total);

        var otra = await bd.AdminNuevoAsync();
        var ajena = await otra.Cliente.PostAsJsonAsync("/api/v1/cotizaciones", Cotizacion(true, acudiente.Id, [new { estudianteId = (Guid?)null, nombre = "X" }]), ClienteApi.Json);
        Assert.Equal(HttpStatusCode.NotFound, ajena.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/v1/cotizaciones/{nueva.Id}")).StatusCode);
    }

    [Fact]
    public async Task Programa_los_cobros_con_un_mensaje_en_uso_por_canal()
    {
        var admin = await bd.AdminNuevoAsync();
        var c = admin.Cliente;
        var (_, acudiente, estudiante) = await c.CrearEstudianteAsync();
        var factura = await c.CrearAsync<FacturaDto>("facturas", DatosFinanzas.Factura(acudiente.Id, estudiante.Id));

        async Task<string> Falla(object cuerpo, HttpStatusCode codigo) =>
            await (await c.PostAsJsonAsync("/api/v1/programaciones", cuerpo, ClienteApi.Json)).MensajeErrorAsync(codigo);
        Assert.Equal("Marca exactamente un mensaje de WhatsApp \"En uso\".",
            await Falla(DatosFinanzas.Programacion(factura.Id, [DatosFinanzas.Plantilla("ACTIVO")]), HttpStatusCode.UnprocessableEntity));
        Assert.Equal("Marca exactamente un correo \"En uso\".",
            await Falla(DatosFinanzas.Programacion(factura.Id, conEmail: true), HttpStatusCode.UnprocessableEntity));
        Assert.Equal("Una programación activa debe enviar por WhatsApp, correo o ambos.",
            await Falla(DatosFinanzas.Programacion(factura.Id, [], conWhatsapp: false), HttpStatusCode.UnprocessableEntity));
        var correoSinAsunto = DatosFinanzas.Programacion(factura.Id, correos: [DatosFinanzas.Plantilla("EN_USO")], conEmail: true);
        Assert.Equal("Escribe el asunto del correo.", await Falla(correoSinAsunto, HttpStatusCode.BadRequest));

        var w1 = DatosFinanzas.Plantilla("EN_USO", "Mensaje 1");
        var w2 = DatosFinanzas.Plantilla("ACTIVO", "Mensaje 2");
        var correo = DatosFinanzas.Plantilla("EN_USO", "Cuerpo", "Recordatorio {mes}", [DatosFinanzas.Adjunto()]);
        var creada = await c.CrearAsync<ProgramacionDto>("programaciones", DatosFinanzas.Programacion(factura.Id, [w1, w2], [correo], conEmail: true));
        Assert.Equal(factura.Numero, creada.NumeroFactura);
        Assert.Equal(["Mensaje 1", "Mensaje 2"], creada.MensajesWhatsapp.Select(m => m.Cuerpo));
        var adjunto = Assert.Single(Assert.Single(creada.Correos).Adjuntos);
        Assert.StartsWith("data:application/pdf;base64,", adjunto.Url);

        Assert.Equal($"La factura {factura.Numero} ya tiene la programación \"Mensualidad Juan\".",
            await Falla(DatosFinanzas.Programacion(factura.Id), HttpStatusCode.Conflict));

        // Cambiar cuál está "En uso" (intercambio) y conservar el adjunto sin reenviarlo.
        var (id1, id2) = (creada.MensajesWhatsapp[0].Id, creada.MensajesWhatsapp[1].Id);
        var correoGuardado = creada.Correos[0];
        var conservado = new { correoGuardado.Adjuntos[0].Nombre, correoGuardado.Adjuntos[0].Tipo, correoGuardado.Adjuntos[0].Tamano, url = "" };
        var edicion = DatosFinanzas.Programacion(factura.Id,
            [DatosFinanzas.Plantilla("ACTIVO", "Mensaje 1", id: id1), DatosFinanzas.Plantilla("EN_USO", "Mensaje 2 editado", id: id2)],
            [DatosFinanzas.Plantilla("EN_USO", "Cuerpo", "Recordatorio {mes}", [conservado], correoGuardado.Id)], conEmail: true);
        var editada = await (await c.PutAsJsonAsync($"/api/v1/programaciones/{creada.Id}", edicion, ClienteApi.Json)).LeerAsync<ProgramacionDto>();
        Assert.Equal([(id1, EstadoPlantilla.Activo), (id2, EstadoPlantilla.EnUso)], editada.MensajesWhatsapp.Select(m => (m.Id, m.Estado)));
        Assert.Single(editada.Correos[0].Adjuntos);

        // Quitar el correo borra su archivo.
        var sinCorreo = DatosFinanzas.Programacion(factura.Id, [DatosFinanzas.Plantilla("EN_USO", id: id2)]);
        var final = await (await c.PutAsJsonAsync($"/api/v1/programaciones/{creada.Id}", sinCorreo, ClienteApi.Json)).LeerAsync<ProgramacionDto>();
        Assert.Equal(id2, Assert.Single(final.MensajesWhatsapp).Id);
        Assert.Empty(final.Correos);
        await using (var db = bd.Contexto(null, modoSistema: true))
        {
            Assert.False(await db.Archivos.AnyAsync(a => a.IdEmpresa == admin.Empresa.Id && a.Nombre == "tarifas.pdf"));
        }

        var motivo = await (await c.GetAsync($"/api/v1/facturas/{factura.Id}/motivo-no-eliminar")).LeerAsync<RespuestaMotivo>();
        Assert.Equal("No se puede eliminar: tiene una programación de facturación. Elimina primero la programación o anula la factura.", motivo.Motivo);
    }

    [Fact]
    public async Task Muestra_el_historial_y_el_conteo_de_envios()
    {
        var admin = await bd.AdminNuevoAsync();
        var c = admin.Cliente;
        var (_, acudiente, estudiante) = await c.CrearEstudianteAsync();
        var factura = await c.CrearAsync<FacturaDto>("facturas", DatosFinanzas.Factura(acudiente.Id, estudiante.Id));
        var programacion = await c.CrearAsync<ProgramacionDto>("programaciones", DatosFinanzas.Programacion(factura.Id));
        await using (var db = bd.Contexto(null, modoSistema: true))
        {
            db.EnviosProgramacion.AddRange(
                new EnvioProgramacion { IdProgramacion = programacion.Id, Fecha = DateTime.UtcNow.AddDays(-40), Canal = CanalEnvio.Whatsapp, Destinatario = "3001234567", Periodo = new DateOnly(2026, 9, 1), Estado = EstadoEnvio.Enviado, Detalle = "Entregado" },
                new EnvioProgramacion { IdProgramacion = programacion.Id, Fecha = DateTime.UtcNow.AddDays(-2), Canal = CanalEnvio.Whatsapp, Destinatario = "3001234567", Periodo = new DateOnly(2026, 10, 1), Estado = EstadoEnvio.Fallido, Detalle = "Sin WhatsApp" });
            await db.SaveChangesAsync();
        }

        var envios = JsonDocument.Parse(await (await c.GetAsync($"/api/v1/programaciones/{programacion.Id}/envios")).Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(2, envios.GetArrayLength());
        Assert.Equal(("FALLIDO", "WHATSAPP", "2026-10"), (envios[0].GetProperty("estado").GetString(), envios[0].GetProperty("canal").GetString(), envios[0].GetProperty("periodo").GetString()));
        var otroId = Guid.NewGuid();
        var conteo = await (await c.GetAsync($"/api/v1/programaciones/envios/conteo?ids={programacion.Id},{otroId}")).LeerAsync<Dictionary<Guid, int>>();
        Assert.Equal(2, conteo[programacion.Id]);
        Assert.Equal(0, conteo[otroId]);

        var tablero = await (await c.GetAsync("/api/v1/tablero")).LeerAsync<TableroDto>();
        Assert.Equal(1, tablero.Pendientes.Single(p => p.Id == "envios-fallidos").Cantidad);

        var otra = await bd.AdminNuevoAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await otra.Cliente.GetAsync($"/api/v1/programaciones/{programacion.Id}/envios")).StatusCode);
        Assert.Equal(0, (await (await otra.Cliente.GetAsync($"/api/v1/programaciones/envios/conteo?ids={programacion.Id}")).LeerAsync<Dictionary<Guid, int>>())[programacion.Id]);

        // Al eliminar la programación se borra su historial.
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/v1/programaciones/{programacion.Id}")).StatusCode);
    }
}
