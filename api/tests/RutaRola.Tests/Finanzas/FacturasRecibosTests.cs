using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Application.Finanzas;
using RutaRola.Application.Personas;
using RutaRola.Tests.Infraestructura;

namespace RutaRola.Tests.Finanzas;

[Collection(ColeccionBaseDatos.Nombre)]
public class FacturasRecibosTests(BaseDatosPrueba bd)
{
    private static async Task<string> FallaAsync(HttpClient c, string recurso, object cuerpo, HttpStatusCode codigo) =>
        await (await c.PostAsJsonAsync($"/api/v1/{recurso}", cuerpo, ClienteApi.Json)).MensajeErrorAsync(codigo);

    [Fact]
    public async Task Crea_facturas_con_consecutivo_snapshot_y_detalle_por_mes()
    {
        var admin = await bd.AdminNuevoAsync();
        var c = admin.Cliente;
        var (_, acudiente, estudiante) = await c.CrearEstudianteAsync();

        var factura = await c.CrearAsync<FacturaDto>("facturas", DatosFinanzas.Factura(acudiente.Id, estudiante.Id, "2026-02", "2026-11"));
        Assert.Equal("FAC-0001", factura.Numero);
        Assert.Equal("María Pérez López", factura.NombreAcudiente);
        Assert.Equal("Juan Sebastián Pérez López", factura.NombreEstudiante);
        Assert.Equal(("2026-02", "2026-11", "ACTIVO"), (factura.PeriodoInicio, factura.PeriodoFin, factura.Estado));
        await using (var db = bd.Contexto(null, modoSistema: true))
        {
            var guardada = await db.Facturas.Include(f => f.Detalle).SingleAsync(f => f.Id == factura.Id);
            Assert.Equal(10, guardada.Detalle.Count);
            Assert.Equal(4_200_000m, guardada.ValorTotal);
        }

        // Anulada no choca; el consecutivo sigue.
        var segunda = await c.CrearAsync<FacturaDto>("facturas", DatosFinanzas.Factura(acudiente.Id, estudiante.Id, "2026-05", "2026-06", estado: "INACTIVO"));
        Assert.Equal("FAC-0002", segunda.Numero);

        // Editar la persona del acudiente no cambia lo emitido.
        var edicion = DatosCatalogos.Acudiente(acudiente.Usuario, acudiente.NumeroDocumento, "Marta", contrasena: "");
        (await c.PutAsJsonAsync($"/api/v1/acudientes/{acudiente.Id}", edicion, ClienteApi.Json)).EnsureSuccessStatusCode();
        Assert.Equal("María Pérez López", (await (await c.GetAsync($"/api/v1/facturas/{factura.Id}")).LeerAsync<FacturaDto>()).NombreAcudiente);

        var lista = await (await c.GetAsync($"/api/v1/facturas?texto=fac-0001&estado=ACTIVO&acudienteId={acudiente.Id}")).LeerAsync<ListaPaginada<FacturaDto>>();
        Assert.Equal(factura.Id, Assert.Single(lista.Items).Id);
        var opciones = await (await c.GetAsync($"/api/v1/facturas/opciones?estudianteId={estudiante.Id}")).LeerAsync<List<Opcion>>();
        Assert.Equal(["FAC-0002 · Juan Sebastián Pérez López", "FAC-0001 · Juan Sebastián Pérez López"], opciones.Select(o => o.Nombre));
    }

    [Fact]
    public async Task Valida_las_reglas_de_la_factura()
    {
        var admin = await bd.AdminNuevoAsync();
        var c = admin.Cliente;
        var (colegio, acudiente, estudiante) = await c.CrearEstudianteAsync();
        var otroAcudiente = await c.CrearAcudienteAsync();

        Assert.Equal("El estudiante no está a cargo del acudiente elegido.",
            await FallaAsync(c, "facturas", DatosFinanzas.Factura(otroAcudiente.Id, estudiante.Id), HttpStatusCode.UnprocessableEntity));

        var invertida = await c.PostAsJsonAsync("/api/v1/facturas", DatosFinanzas.Factura(acudiente.Id, estudiante.Id, "2026-11", "2026-02"), ClienteApi.Json);
        Assert.Equal("El mes final no puede ser anterior al mes inicial.", await invertida.MensajeErrorAsync(HttpStatusCode.BadRequest));

        await c.CrearAsync<FacturaDto>("facturas", DatosFinanzas.Factura(acudiente.Id, estudiante.Id, "2026-02", "2026-06"));
        Assert.Equal("Juan Sebastián Pérez López ya tiene la factura vigente FAC-0001 en ese periodo. Anúlala o ajusta los meses.",
            await FallaAsync(c, "facturas", DatosFinanzas.Factura(acudiente.Id, estudiante.Id, "2026-06", "2026-11"), HttpStatusCode.Conflict));
        // Meses que no se cruzan sí.
        await c.CrearAsync<FacturaDto>("facturas", DatosFinanzas.Factura(acudiente.Id, estudiante.Id, "2026-07", "2026-11"));

        var otra = await bd.AdminNuevoAsync();
        Assert.Equal("El acudiente no existe o fue eliminado.",
            await FallaAsync(otra.Cliente, "facturas", DatosFinanzas.Factura(acudiente.Id, estudiante.Id), HttpStatusCode.NotFound));

        // El estudiante con facturas no se elimina.
        var motivo = await (await c.GetAsync($"/api/v1/estudiantes/{estudiante.Id}/motivo-no-eliminar")).LeerAsync<RespuestaMotivo>();
        Assert.Equal("No se puede eliminar: tiene 2 factura(s) registradas. Cámbialo a Inactivo para conservar el historial.", motivo.Motivo);
    }

    [Fact]
    public async Task Registra_recibos_por_mes_y_bloquea_eliminar_la_factura()
    {
        var admin = await bd.AdminNuevoAsync();
        var c = admin.Cliente;
        var (_, acudiente, estudiante) = await c.CrearEstudianteAsync();
        var factura = await c.CrearAsync<FacturaDto>("facturas", DatosFinanzas.Factura(acudiente.Id, estudiante.Id, "2026-02", "2026-11"));

        var recibo = await c.CrearAsync<ReciboDto>("recibos", DatosFinanzas.Recibo(factura.Id, "2026-02", fechaPago: "2026-02-04"));
        Assert.Equal(("REC-0001", "FAC-0001", "Juan Sebastián Pérez López", "2026-02"), (recibo.Numero, recibo.NumeroFactura, recibo.NombreEstudiante, recibo.Periodo));
        var json = JsonDocument.Parse(await (await c.GetAsync($"/api/v1/recibos/{recibo.Id}")).Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("NEQUI", json.GetProperty("medioPago").GetString());

        Assert.Equal("febrero de 2026 de la factura FAC-0001 ya está pagado (recibo REC-0001).",
            await FallaAsync(c, "recibos", DatosFinanzas.Recibo(factura.Id, "2026-02"), HttpStatusCode.Conflict));
        Assert.Equal("diciembre de 2026 no está dentro del periodo de la factura FAC-0001.",
            await FallaAsync(c, "recibos", DatosFinanzas.Recibo(factura.Id, "2026-12"), HttpStatusCode.UnprocessableEntity));
        var futura = await c.PostAsJsonAsync("/api/v1/recibos", DatosFinanzas.Recibo(factura.Id, "2026-03", fechaPago: DatosOperacion.Hoy.AddDays(2).ToString("yyyy-MM-dd")), ClienteApi.Json);
        Assert.Equal("La fecha de pago no puede ser futura.", await futura.MensajeErrorAsync(HttpStatusCode.BadRequest));

        // Anular el recibo libera el mes.
        var anulado = DatosFinanzas.Recibo(factura.Id, "2026-02", estado: "INACTIVO", fechaPago: "2026-02-04");
        (await c.PutAsJsonAsync($"/api/v1/recibos/{recibo.Id}", anulado, ClienteApi.Json)).EnsureSuccessStatusCode();
        var nuevo = await c.CrearAsync<ReciboDto>("recibos", DatosFinanzas.Recibo(factura.Id, "2026-02", medio: "EFECTIVO", fechaPago: "2026-02-05"));
        Assert.Equal("REC-0002", nuevo.Numero);

        var filtrados = await (await c.GetAsync($"/api/v1/recibos?facturaId={factura.Id}&medioPago=EFECTIVO&estado=ACTIVO&desde=2026-02-01&hasta=2026-02-28"))
            .LeerAsync<ListaPaginada<ReciboDto>>();
        Assert.Equal(nuevo.Id, Assert.Single(filtrados.Items).Id);
        Assert.Equal(2, (await (await c.GetAsync("/api/v1/recibos?texto=48210137")).LeerAsync<ListaPaginada<ReciboDto>>()).Total);

        var motivo = await (await c.GetAsync($"/api/v1/facturas/{factura.Id}/motivo-no-eliminar")).LeerAsync<RespuestaMotivo>();
        Assert.Equal("No se puede eliminar: tiene 2 recibo(s) de pago. Anúlala para conservar el historial.", motivo.Motivo);
        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"/api/v1/facturas/{factura.Id}")).StatusCode);

        // Con la factura anulada no se reciben pagos.
        var anulada = DatosFinanzas.Factura(acudiente.Id, estudiante.Id, "2026-02", "2026-11", estado: "INACTIVO");
        (await c.PutAsJsonAsync($"/api/v1/facturas/{factura.Id}", anulada, ClienteApi.Json)).EnsureSuccessStatusCode();
        Assert.Equal("La factura FAC-0001 está anulada.",
            await FallaAsync(c, "recibos", DatosFinanzas.Recibo(factura.Id, "2026-03"), HttpStatusCode.UnprocessableEntity));

        // Auditoría de facturas y pagos.
        await using var db = bd.Contexto(null, modoSistema: true);
        Assert.True(await db.Auditoria.AnyAsync(a => a.IdRegistro == recibo.Id));
        Assert.True(await db.Auditoria.AnyAsync(a => a.IdRegistro == factura.Id));
    }

    [Fact]
    public async Task A_no_ve_ni_usa_facturas_de_B_y_el_conductor_no_entra_a_finanzas()
    {
        var a = await bd.AdminNuevoAsync();
        var b = await bd.EscenarioAsync(estudiantes: 1);
        var factura = await b.Admin.Cliente.CrearAsync<FacturaDto>("facturas", DatosFinanzas.Factura(b.Acudiente.Id, b.Estudiantes[0].Id));

        Assert.Empty((await (await a.Cliente.GetAsync("/api/v1/facturas")).LeerAsync<ListaPaginada<FacturaDto>>()).Items);
        Assert.Equal(HttpStatusCode.NotFound, (await a.Cliente.GetAsync($"/api/v1/facturas/{factura.Id}")).StatusCode);
        Assert.Equal("La factura no existe o fue eliminada.",
            await FallaAsync(a.Cliente, "recibos", DatosFinanzas.Recibo(factura.Id, DatosFinanzas.Periodo()), HttpStatusCode.NotFound));
        // El consecutivo es por empresa.
        var (_, acudienteA, estudianteA) = await a.Cliente.CrearEstudianteAsync();
        Assert.Equal("FAC-0001", (await a.Cliente.CrearAsync<FacturaDto>("facturas", DatosFinanzas.Factura(acudienteA.Id, estudianteA.Id))).Numero);

        foreach (var recurso in new[] { "facturas", "recibos", "cotizaciones", "programaciones", "reportes/morosos", "tablero" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await b.ClienteConductor.GetAsync($"/api/v1/{recurso}")).StatusCode);
        }
    }
}
