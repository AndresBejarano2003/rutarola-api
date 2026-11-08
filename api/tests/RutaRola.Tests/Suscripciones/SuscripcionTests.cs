using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Autenticacion;
using RutaRola.Application.Suscripciones;
using RutaRola.Domain.Enums;
using RutaRola.Tests.Infraestructura;

namespace RutaRola.Tests.Suscripciones;

/// <summary>Planes, prueba gratuita, límites y pagos en línea con Wompi (firma del checkout y eventos firmados).</summary>
[Collection(ColeccionBaseDatos.Nombre)]
public class SuscripcionTests(BaseDatosPrueba bd)
{
    private static string Sha256(string texto) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(texto))).ToLowerInvariant();

    /// <summary>Evento <c>transaction.updated</c> firmado como lo hace Wompi.</summary>
    private static object Evento(string referencia, long centavos, string estado = "APPROVED", string? secreto = null)
    {
        var id = $"1234-{Guid.NewGuid():N}"[..20];
        const long marca = 1_730_000_000;
        var checksum = Sha256($"{id}{estado}{centavos}{marca}{secreto ?? BaseDatosPrueba.SecretoEventosWompi}").ToUpperInvariant();
        return new
        {
            @event = "transaction.updated",
            data = new
            {
                transaction = new
                {
                    id, amount_in_cents = centavos, reference = referencia, customer_email = "pagos@empresa.co", currency = "COP",
                    payment_method_type = "NEQUI", status = estado,
                },
            },
            environment = "test",
            signature = new { properties = new[] { "transaction.id", "transaction.status", "transaction.amount_in_cents" }, checksum },
            timestamp = marca,
            sent_at = "2026-11-06T12:00:00.000Z",
        };
    }

    private Task<HttpResponseMessage> EnviarEventoAsync(object evento) =>
        bd.Api.CreateClient().PostAsJsonAsync("/api/v1/pagos/wompi/eventos", evento, ClienteApi.Json);

    [Fact]
    public async Task Una_empresa_nueva_tiene_tres_meses_de_Profesional_y_los_planes_son_publicos()
    {
        var planes = await (await bd.Api.CreateClient().GetAsync("/api/v1/suscripcion/planes")).LeerAsync<List<PlanDto>>();
        Assert.Equal(["Gratis", "Esencial", "Profesional", "Empresarial"], planes.Select(p => p.Nombre));
        var esencial = planes.Single(p => p.Id == PlanSuscripcion.Esencial);
        Assert.Equal((89_900, 899_000), (esencial.PrecioMensual, esencial.PrecioAnual));
        Assert.Null(planes.Single(p => p.Id == PlanSuscripcion.Empresarial).Limites.Vehiculos);

        var admin = await bd.AdminNuevoAsync();
        var s = await (await admin.Cliente.GetAsync("/api/v1/suscripcion")).LeerAsync<SuscripcionDto>();
        Assert.Equal((PlanSuscripcion.Profesional, EstadoSuscripcion.Prueba), (s.Plan, s.Estado));
        Assert.InRange(s.DiasRestantes!.Value, 89, 93);
        Assert.Contains(FuncionPlan.GpsEnVivo, s.Funciones);
        Assert.True(s.PagosEnLinea);
        Assert.Equal((15, 400), (s.Limites.Vehiculos!.Value, s.Limites.Estudiantes!.Value));
    }

    [Fact]
    public async Task El_pago_aprobado_y_firmado_activa_el_plan_una_sola_vez()
    {
        var admin = await bd.AdminNuevoAsync();
        var prueba = await (await admin.Cliente.GetAsync("/api/v1/suscripcion")).LeerAsync<SuscripcionDto>();

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Cliente.PostAsJsonAsync("/api/v1/suscripcion/pagos",
            new { plan = "GRATIS", periodo = "MENSUAL" }, ClienteApi.Json)).StatusCode);
        var checkout = await (await admin.Cliente.PostAsJsonAsync("/api/v1/suscripcion/pagos",
            new { plan = "ESENCIAL", periodo = "ANUAL" }, ClienteApi.Json)).LeerAsync<CheckoutDto>(HttpStatusCode.Created);
        Assert.Equal(89_900L * 10 * 100, checkout.ValorCentavos);
        Assert.Equal(Sha256($"{checkout.Referencia}{checkout.ValorCentavos}COP{BaseDatosPrueba.SecretoIntegridadWompi}"), checkout.Firma);
        Assert.StartsWith("https://checkout.wompi.co/p/?public-key=pub_test_prueba", checkout.Url);
        Assert.Contains($"reference={checkout.Referencia}", checkout.Url);
        Assert.Contains("signature%3Aintegrity=", checkout.Url.Replace("signature:integrity", "signature%3Aintegrity"));

        // Firma inválida → 401; valor distinto → se ignora.
        Assert.Equal(HttpStatusCode.Unauthorized, (await EnviarEventoAsync(Evento(checkout.Referencia, checkout.ValorCentavos, secreto: "otro"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await EnviarEventoAsync(Evento(checkout.Referencia, 100))).StatusCode);
        var pendiente = Assert.Single(await (await admin.Cliente.GetAsync("/api/v1/suscripcion/pagos")).LeerAsync<List<PagoSuscripcionDto>>());
        Assert.Equal(EstadoPagoSuscripcion.Pendiente, pendiente.Estado);

        // Aprobado: plan Esencial anual; los días de prueba que quedaban se suman.
        Assert.Equal(HttpStatusCode.OK, (await EnviarEventoAsync(Evento(checkout.Referencia, checkout.ValorCentavos))).StatusCode);
        var activa = await (await admin.Cliente.GetAsync("/api/v1/suscripcion")).LeerAsync<SuscripcionDto>();
        Assert.Equal((PlanSuscripcion.Esencial, EstadoSuscripcion.Activa, "ANUAL"), (activa.Plan, activa.Estado, activa.Periodo));
        Assert.Equal(prueba.FechaFin!.Value.AddYears(1), activa.FechaFin!.Value, TimeSpan.FromSeconds(5));
        Assert.DoesNotContain(FuncionPlan.GpsEnVivo, activa.Funciones);
        var pago = Assert.Single(await (await admin.Cliente.GetAsync("/api/v1/suscripcion/pagos")).LeerAsync<List<PagoSuscripcionDto>>());
        Assert.Equal((EstadoPagoSuscripcion.Aprobado, "NEQUI", 899_000L), (pago.Estado, pago.MetodoPago, pago.Valor));

        // Repetido (Wompi reintenta) o un DECLINED tardío no cambian nada.
        await EnviarEventoAsync(Evento(checkout.Referencia, checkout.ValorCentavos));
        await EnviarEventoAsync(Evento(checkout.Referencia, checkout.ValorCentavos, "DECLINED"));
        var igual = await (await admin.Cliente.GetAsync("/api/v1/suscripcion")).LeerAsync<SuscripcionDto>();
        Assert.Equal(activa.FechaFin, igual.FechaFin);
        Assert.Equal(EstadoPagoSuscripcion.Aprobado, (await (await admin.Cliente.PostAsJsonAsync(
            $"/api/v1/suscripcion/pagos/{checkout.Referencia}/verificacion", new { idTransaccion = "" }, ClienteApi.Json)).LeerAsync<PagoSuscripcionDto>()).Estado);

        // Otra empresa no ve ni verifica ese pago.
        var otra = await bd.AdminNuevoAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await otra.Cliente.PostAsJsonAsync(
            $"/api/v1/suscripcion/pagos/{checkout.Referencia}/verificacion", new { idTransaccion = "" }, ClienteApi.Json)).StatusCode);
        Assert.Empty(await (await otra.Cliente.GetAsync("/api/v1/suscripcion/pagos")).LeerAsync<List<PagoSuscripcionDto>>());
    }

    [Fact]
    public async Task Al_vencer_pasa_a_Gratis_y_limita_crear_por_encima_del_plan()
    {
        var admin = await bd.AdminNuevoAsync();
        var c = admin.Cliente;
        await c.CrearAsync<object>("vehiculos", DatosCatalogos.Vehiculo($"ABZ{Random.Shared.Next(100, 999)}", "V-1"));
        await (await c.GetAsync("/api/v1/suscripcion")).LeerAsync<SuscripcionDto>();
        await using (var db = bd.Contexto(null, modoSistema: true))
        {
            await db.Suscripciones.IgnoreQueryFilters().Where(s => s.IdEmpresa == admin.Empresa.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.FechaFin, DateTime.UtcNow.AddMinutes(-1)));
        }

        var gratis = await (await c.GetAsync("/api/v1/suscripcion")).LeerAsync<SuscripcionDto>();
        Assert.Equal((PlanSuscripcion.Gratis, EstadoSuscripcion.Gratis, (DateTime?)null), (gratis.Plan, gratis.Estado, gratis.FechaFin));
        Assert.Equal(1, gratis.Uso.Vehiculos);
        var mensaje = await (await c.PostAsJsonAsync("/api/v1/vehiculos", DatosCatalogos.Vehiculo($"CDZ{Random.Shared.Next(100, 999)}", "V-2"), ClienteApi.Json))
            .MensajeErrorAsync(HttpStatusCode.UnprocessableEntity);
        Assert.Contains("permite 1 vehículo", mensaje);

        // La plataforma lo ajusta a mano (p. ej. pago por transferencia).
        var super = await bd.SuperAdminAsync();
        var url = $"/api/v1/empresas/{admin.Empresa.Id}/suscripcion";
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await super.PutAsJsonAsync(url,
            new { plan = "PROFESIONAL", estado = "ACTIVA", periodo = "MENSUAL", fechaFin = DateTime.UtcNow.AddDays(-1) }, ClienteApi.Json)).StatusCode);
        var ajustada = await (await super.PutAsJsonAsync(url,
            new { plan = "PROFESIONAL", estado = "ACTIVA", periodo = "MENSUAL", fechaFin = DateTime.UtcNow.AddDays(30), nota = "Transferencia" }, ClienteApi.Json))
            .LeerAsync<SuscripcionDto>();
        Assert.Equal((PlanSuscripcion.Profesional, "Transferencia"), (ajustada.Plan, ajustada.Nota));
        await c.CrearAsync<object>("vehiculos", DatosCatalogos.Vehiculo($"EFZ{Random.Shared.Next(100, 999)}", "V-3"));
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PutAsJsonAsync(url,
            new { plan = "EMPRESARIAL", estado = "ACTIVA", fechaFin = DateTime.UtcNow.AddYears(5) }, ClienteApi.Json)).StatusCode);
    }

    [Fact]
    public async Task El_registro_publico_crea_la_empresa_con_la_prueba_y_deja_la_sesion_iniciada()
    {
        var anonimo = bd.Api.CreateClient();
        var usuario = ClienteApi.Unico("reg");
        var nit = ClienteApi.NitUnico();
        object Solicitud(bool acepta = true, string? nitUsado = null) => new
        {
            empresa = new { nombre = "Transportes La Sabana", nit = nitUsado ?? nit, ciudad = "Chía", telefono = "3001234567" },
            administrador = new { nombre = "Carolina Ruiz", usuario, email = $"{usuario}@correo.co", contrasena = "Clave-123" },
            aceptaTerminos = acepta,
        };

        Assert.Equal(HttpStatusCode.BadRequest, (await anonimo.PostAsJsonAsync("/api/v1/auth/registro", Solicitud(acepta: false), ClienteApi.Json)).StatusCode);
        var login = await (await anonimo.PostAsJsonAsync("/api/v1/auth/registro", Solicitud(), ClienteApi.Json)).LeerAsync<RespuestaLogin>(HttpStatusCode.Created);
        Assert.Equal(("ADMINISTRADOR", usuario), (login.Sesion.Rol, login.Sesion.Usuario));
        var cliente = bd.Cliente(login.Tokens.AccessToken);
        var s = await (await cliente.GetAsync("/api/v1/suscripcion")).LeerAsync<SuscripcionDto>();
        Assert.Equal(EstadoSuscripcion.Prueba, s.Estado);

        // NIT repetido.
        Assert.Equal(HttpStatusCode.Conflict, (await anonimo.PostAsJsonAsync("/api/v1/auth/registro", Solicitud(), ClienteApi.Json)).StatusCode);
    }
}
