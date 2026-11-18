using System.Net;
using System.Net.Http.Json;
using RutaRola.Application.Comun;
using RutaRola.Application.Finanzas;
using RutaRola.Tests.Infraestructura;

namespace RutaRola.Tests.Finanzas;

/// <summary>Recibos de varios meses, abonos parciales, retiros y cotizaciones que se convierten en facturas.</summary>
[Collection(ColeccionBaseDatos.Nombre)]
public class AbonosRetirosCotizacionesTests(BaseDatosPrueba bd)
{
    private static Dictionary<string, object?> Recibo(Guid facturaId, decimal valor, string aplicacion, params string[] periodos)
    {
        var r = DatosFinanzas.Recibo(facturaId, periodos.FirstOrDefault() ?? "", valor, fechaPago: "2026-02-04");
        r["aplicacion"] = aplicacion;
        r["periodos"] = periodos;
        return r;
    }

    private static async Task<string> FallaAsync(HttpClient c, object cuerpo, HttpStatusCode codigo) =>
        await (await c.PostAsJsonAsync("/api/v1/recibos", cuerpo, ClienteApi.Json)).MensajeErrorAsync(codigo);

    private static async Task<IReadOnlyList<CuotaFacturaDto>> CuotasAsync(HttpClient c, Guid factura) =>
        await (await c.GetAsync($"/api/v1/facturas/{factura}/cuotas")).LeerAsync<List<CuotaFacturaDto>>();

    [Fact]
    public async Task Un_pago_parcial_deja_el_mes_con_saldo_y_un_recibo_paga_varios_meses()
    {
        var admin = await bd.AdminNuevoAsync();
        var c = admin.Cliente;
        var (_, acudiente, estudiante) = await c.CrearEstudianteAsync();
        var factura = await c.CrearAsync<FacturaDto>("facturas", DatosFinanzas.Factura(acudiente.Id, estudiante.Id, "2026-02", "2026-06", valor: 230000));

        // 130 000 de 230 000: febrero queda pendiente con saldo de 100 000.
        var parcial = await c.CrearAsync<ReciboDto>("recibos", Recibo(factura.Id, 130000, "MESES", "2026-02"));
        Assert.Equal(("2026-02", 130000m), (Assert.Single(parcial.Meses).Periodo, parcial.Meses[0].Valor));
        var febrero = (await CuotasAsync(c, factura.Id))[0];
        Assert.Equal((130000m, 100000m), (febrero.Pagado, febrero.Saldo));
        Assert.NotEqual("PAGADA", febrero.Estado);

        // Elegir febrero y marzo con 330 000: completa febrero y paga marzo.
        var dos = await c.CrearAsync<ReciboDto>("recibos", Recibo(factura.Id, 330000, "MESES", "2026-03", "2026-02"));
        Assert.Equal([("2026-02", 100000m), ("2026-03", 230000m)], dos.Meses.Select(m => (m.Periodo, m.Valor)));
        Assert.Equal("2026-02", dos.Periodo);
        Assert.Equal("febrero de 2026 de la factura FAC-0001 ya está pagado.", await FallaAsync(c, Recibo(factura.Id, 10000, "MESES", "2026-02"), HttpStatusCode.Conflict));
        Assert.Equal("El valor supera el saldo de los meses elegidos ($ 230.000).",
            (await FallaAsync(c, Recibo(factura.Id, 300000, "MESES", "2026-04"), HttpStatusCode.UnprocessableEntity)).Replace(' ', ' '));

        // Abono al saldo de la factura: 300 000 → paga abril y abona 70 000 a mayo.
        var abono = await c.CrearAsync<ReciboDto>("recibos", Recibo(factura.Id, 300000, "SALDO"));
        Assert.Equal([("2026-04", 230000m), ("2026-05", 70000m)], abono.Meses.Select(m => (m.Periodo, m.Valor)));
        Assert.Contains("factura FAC-0001", await FallaAsync(c, Recibo(factura.Id, 400000, "SALDO"), HttpStatusCode.UnprocessableEntity));

        // Anular libera lo aplicado.
        var anulado = Recibo(factura.Id, 300000, "SALDO");
        anulado["estado"] = "INACTIVO";
        (await c.PutAsJsonAsync($"/api/v1/recibos/{abono.Id}", anulado, ClienteApi.Json)).EnsureSuccessStatusCode();
        var cuotas = await CuotasAsync(c, factura.Id);
        Assert.Equal([0m, 0m, 230000m, 230000m, 230000m], cuotas.Select(x => x.Saldo));
    }

    [Fact]
    public async Task El_retiro_deja_de_cobrar_los_meses_que_faltan()
    {
        var admin = await bd.AdminNuevoAsync();
        var c = admin.Cliente;
        var (_, acudiente, estudiante) = await c.CrearEstudianteAsync();
        var factura = await c.CrearAsync<FacturaDto>("facturas", DatosFinanzas.Factura(acudiente.Id, estudiante.Id, "2026-02", "2026-11", valor: 200000));
        await c.CrearAsync<ReciboDto>("recibos", Recibo(factura.Id, 200000, "MESES", "2026-03"));

        var conPago = await c.PutAsJsonAsync($"/api/v1/facturas/{factura.Id}/retiro", new { periodoRetiro = "2026-03", motivo = "Cambio de colegio" }, ClienteApi.Json);
        Assert.StartsWith("La factura FAC-0001 tiene pagos de marzo de 2026.", await conPago.MensajeErrorAsync(HttpStatusCode.UnprocessableEntity));
        var primerMes = await c.PutAsJsonAsync($"/api/v1/facturas/{factura.Id}/retiro", new { periodoRetiro = "2026-02", motivo = "x" }, ClienteApi.Json);
        Assert.Contains("anula la factura", await primerMes.MensajeErrorAsync(HttpStatusCode.UnprocessableEntity));

        var retirada = await (await c.PutAsJsonAsync($"/api/v1/facturas/{factura.Id}/retiro", new { periodoRetiro = "2026-06", motivo = "Cambio de colegio" }, ClienteApi.Json))
            .LeerAsync<FacturaDto>();
        Assert.Equal(("2026-06", "Cambio de colegio", 800000m), (retirada.PeriodoRetiro, retirada.MotivoRetiro, retirada.ValorTotal));
        Assert.Equal(["2026-02", "2026-03", "2026-04", "2026-05"], (await CuotasAsync(c, factura.Id)).Select(x => x.Periodo));
        Assert.Equal("junio de 2026 no se cobra en la factura FAC-0001.",
            await FallaAsync(c, Recibo(factura.Id, 200000, "MESES", "2026-06"), HttpStatusCode.UnprocessableEntity));

        // Quitar el retiro vuelve a cobrar todo.
        var sinRetiro = await (await c.PutAsJsonAsync($"/api/v1/facturas/{factura.Id}/retiro", new { periodoRetiro = "", motivo = "" }, ClienteApi.Json)).LeerAsync<FacturaDto>();
        Assert.Equal(("", 2000000m), (sinRetiro.PeriodoRetiro, sinRetiro.ValorTotal));
    }

    [Fact]
    public async Task Inactivar_al_estudiante_retira_sus_facturas_desde_el_mes_siguiente()
    {
        var admin = await bd.AdminNuevoAsync();
        var c = admin.Cliente;
        var (_, acudiente, estudiante) = await c.CrearEstudianteAsync();
        var factura = await c.CrearAsync<FacturaDto>("facturas",
            DatosFinanzas.Factura(acudiente.Id, estudiante.Id, DatosFinanzas.Periodo(-2), DatosFinanzas.Periodo(4), valor: 100000));

        var solicitud = DatosCatalogos.Estudiante(acudiente.Id, estudiante.ColegioId, documento: estudiante.NumeroDocumento);
        solicitud["estado"] = "INACTIVO";
        (await c.PutAsJsonAsync($"/api/v1/estudiantes/{estudiante.Id}", solicitud, ClienteApi.Json)).EnsureSuccessStatusCode();

        var retirada = await (await c.GetAsync($"/api/v1/facturas/{factura.Id}")).LeerAsync<FacturaDto>();
        Assert.Equal((DatosFinanzas.Periodo(1), "Retiro del estudiante (quedó inactivo).", 300000m),
            (retirada.PeriodoRetiro, retirada.MotivoRetiro, retirada.ValorTotal));
    }

    [Fact]
    public async Task La_cotizacion_aprobada_se_convierte_en_facturas()
    {
        var admin = await bd.AdminNuevoAsync();
        var c = admin.Cliente;
        var (colegio, acudiente, estudiante) = await c.CrearEstudianteAsync();

        // Cliente nuevo: varios colegios y valores distintos; no se factura sin registrarlos.
        var nueva = await c.CrearAsync<CotizacionDto>("cotizaciones", new
        {
            fecha = DatosOperacion.Hoy, registrados = false, acudienteId = "", nombreAcudiente = "Camila Torres", telefonoAcudiente = "3209988776",
            estudiantes = new object[]
            {
                new { estudianteId = (Guid?)null, nombre = "Lucía Torres", colegioId = (Guid?)colegio.Id, nombreColegio = "", valor = 380000 },
                new { estudianteId = (Guid?)null, nombre = "Miguel Torres", colegioId = (Guid?)null, nombreColegio = "Liceo Los Andes", valor = 350000 },
            },
            periodoInicio = "2026-02", periodoFin = "2026-11", estado = "PENDIENTE", observaciones = "",
        });
        Assert.Equal((730000m, 10, 7300000m), (nueva.ValorMensual, nueva.Meses, nueva.ValorTotal));
        Assert.Equal([colegio.RazonSocial, "Liceo Los Andes"], nueva.Estudiantes.Select(e => e.NombreColegio));
        var sinRegistro = await c.PostAsJsonAsync($"/api/v1/cotizaciones/{nueva.Id}/facturar", new { }, ClienteApi.Json);
        Assert.Contains("registra al acudiente", await sinRegistro.MensajeErrorAsync(HttpStatusCode.UnprocessableEntity));

        var registrada = await c.CrearAsync<CotizacionDto>("cotizaciones", new
        {
            fecha = DatosOperacion.Hoy, registrados = true, acudienteId = acudiente.Id, nombreAcudiente = "", telefonoAcudiente = "",
            estudiantes = new[] { new { estudianteId = (Guid?)estudiante.Id, nombre = "x", colegioId = (Guid?)null, nombreColegio = "", valor = 410000 } },
            periodoInicio = "2026-02", periodoFin = "2026-11", estado = "PENDIENTE", observaciones = "",
        });
        var resultado = await (await c.PostAsJsonAsync($"/api/v1/cotizaciones/{registrada.Id}/facturar", new { }, ClienteApi.Json))
            .LeerAsync<ResultadoFacturacion>();
        var factura = Assert.Single(resultado.Facturas);
        Assert.Equal((410000m, "2026-02", "2026-11", "COT-0002"), (factura.ValorContratacion, factura.PeriodoInicio, factura.PeriodoFin, factura.NumeroCotizacion));
        Assert.Equal(RutaRola.Domain.Enums.EstadoCotizacion.Aprobada, resultado.Cotizacion.Estado);
        Assert.Single(resultado.Cotizacion.Facturas);

        // Ya facturada: no se factura dos veces ni se edita ni se elimina.
        var otraVez = await c.PostAsJsonAsync($"/api/v1/cotizaciones/{registrada.Id}/facturar", new { }, ClienteApi.Json);
        Assert.Equal("La cotización COT-0002 ya se convirtió en facturas.", await otraVez.MensajeErrorAsync(HttpStatusCode.Conflict));
        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"/api/v1/cotizaciones/{registrada.Id}")).StatusCode);

        // Una segunda cotización del mismo estudiante choca con la factura vigente: no crea ninguna.
        var cruzada = await c.CrearAsync<CotizacionDto>("cotizaciones", new
        {
            fecha = DatosOperacion.Hoy, registrados = true, acudienteId = acudiente.Id, nombreAcudiente = "", telefonoAcudiente = "",
            estudiantes = new[] { new { estudianteId = (Guid?)estudiante.Id, nombre = "x", colegioId = (Guid?)null, nombreColegio = "", valor = 1000 } },
            periodoInicio = "2026-05", periodoFin = "2026-06", estado = "APROBADA", observaciones = "",
        });
        Assert.Equal(HttpStatusCode.Conflict,
            (await c.PostAsJsonAsync($"/api/v1/cotizaciones/{cruzada.Id}/facturar", new { }, ClienteApi.Json)).StatusCode);
        Assert.Equal(1, (await (await c.GetAsync("/api/v1/facturas")).LeerAsync<ListaPaginada<FacturaDto>>()).Total);
    }
}
