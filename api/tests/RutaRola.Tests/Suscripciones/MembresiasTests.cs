using RutaRola.Api.Seguridad;
using RutaRola.Application.Comunicacion;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Autenticacion;
using RutaRola.Application.Comun;
using RutaRola.Application.Empresas;
using RutaRola.Application.Operacion;
using RutaRola.Application.Suscripciones;
using RutaRola.Domain.Enums;
using RutaRola.Tests.Infraestructura;

namespace RutaRola.Tests.Suscripciones;

/// <summary>Días de prueba parametrizables, decisión al vencer, límites al bajar de plan, funciones por plan y membresía en la plataforma.</summary>
[Collection(ColeccionBaseDatos.Nombre)]
public class MembresiasTests(BaseDatosPrueba bd)
{
    private const string UrlDias = "/api/v1/plataforma/parametros/suscripcion.dias_prueba";

    /// <summary>El último día del plan fue ayer (Colombia): hoy ya pasa a Gratis.</summary>
    private Task VencerAsync(Guid idEmpresa) => FinEnAsync(idEmpresa, -1);

    /// <summary>Deja el último día del plan a <paramref name="dias"/> días de hoy (Colombia).</summary>
    private async Task FinEnAsync(Guid idEmpresa, int dias)
    {
        var hoy = Fechas.Hoy(TimeProvider.System);
        var fin = DateTime.SpecifyKind(hoy.AddDays(dias).ToDateTime(new TimeOnly(12, 0)) - Fechas.DesfaseColombia, DateTimeKind.Utc);
        await using var db = bd.Contexto(null, modoSistema: true);
        await db.Suscripciones.IgnoreQueryFilters().Where(s => s.IdEmpresa == idEmpresa)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.FechaFin, fin));
    }

    /// <summary>Corre el proceso del servidor de la empresa (vencimiento + notificaciones) y lista las del cliente.</summary>
    private async Task<List<NotificacionDto>> NotificacionesAsync(Guid idEmpresa, HttpClient c)
    {
        await bd.EnScope(async sp =>
        {
            sp.GetRequiredService<ContextoUsuarioHttp>().Usar(new ClaimsPrincipal(
                new ClaimsIdentity([new Claim(Reclamos.IdEmpresa, idEmpresa.ToString())], "ProcesoServidor")));
            await sp.GetRequiredService<ServicioSuscripcion>().RevisarVencimientoAsync();
            return await sp.GetRequiredService<GeneradorNotificaciones>().GenerarAsync();
        });
        return (await (await c.GetAsync("/api/v1/notificaciones?tamano=100")).LeerAsync<ListaPaginada<NotificacionDto>>()).Items.ToList();
    }

    [Fact]
    public async Task Los_dias_de_prueba_los_fija_la_plataforma_y_cada_empresa_conserva_los_suyos()
    {
        var super = await bd.SuperAdminAsync();
        var anonimo = bd.Api.CreateClient();
        Assert.Equal(15, (await (await anonimo.GetAsync("/api/v1/suscripcion/condiciones")).LeerAsync<CondicionesRegistroDto>()).DiasPrueba);
        try
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await super.PutAsJsonAsync(UrlDias, new { valor = "0" }, ClienteApi.Json)).StatusCode);
            var admin = await bd.AdminNuevoAsync();
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.Cliente.PutAsJsonAsync(UrlDias, new { valor = "20" }, ClienteApi.Json)).StatusCode);
            var con15 = await (await admin.Cliente.GetAsync("/api/v1/suscripcion")).LeerAsync<SuscripcionDto>();
            Assert.Equal(15, con15.DiasPrueba);

            var cambiado = await (await super.PutAsJsonAsync(UrlDias, new { valor = "20" }, ClienteApi.Json)).LeerAsync<ParametroPlataformaDto>();
            Assert.Equal("20", cambiado.Valor);
            Assert.Equal(20, (await (await anonimo.GetAsync("/api/v1/suscripcion/condiciones")).LeerAsync<CondicionesRegistroDto>()).DiasPrueba);
            var nueva = await bd.AdminNuevoAsync();
            var con20 = await (await nueva.Cliente.GetAsync("/api/v1/suscripcion")).LeerAsync<SuscripcionDto>();
            Assert.Equal((20, EstadoSuscripcion.Prueba), (con20.DiasPrueba, con20.Estado));
            Assert.InRange(con20.DiasRestantes!.Value, 19, 20);

            // La que empezó con 15 los conserva.
            var igual = await (await admin.Cliente.GetAsync("/api/v1/suscripcion")).LeerAsync<SuscripcionDto>();
            Assert.Equal((15, con15.FechaFin), (igual.DiasPrueba, igual.FechaFin));
        }
        finally
        {
            await super.PutAsJsonAsync(UrlDias, new { valor = "15" }, ClienteApi.Json);
        }
    }

    [Fact]
    public async Task Al_dia_siguiente_del_vencimiento_queda_en_Gratis_e_inactiva_lo_que_sobra()
    {
        var admin = await bd.AdminNuevoAsync();
        var c = admin.Cliente;
        var vehiculos = new List<VehiculoDto>();
        for (var i = 1; i <= 3; i++)
        {
            vehiculos.Add(await c.CrearAsync<VehiculoDto>("vehiculos", DatosCatalogos.Vehiculo(DatosOperacion.Placa(), $"{i}")));
        }
        // El último día sigue vigente (0 días) y la plataforma la ve por vencer.
        await FinEnAsync(admin.Empresa.Id, 0);
        var ultimoDia = await (await c.GetAsync("/api/v1/suscripcion")).LeerAsync<SuscripcionDto>();
        Assert.Equal((PlanSuscripcion.Profesional, EstadoSuscripcion.Prueba, 0), (ultimoDia.Plan, ultimoDia.Estado, ultimoDia.DiasRestantes!.Value));
        var super = await bd.SuperAdminAsync();
        EmpresaDto Fila(ListaPaginada<EmpresaDto> l) => Assert.Single(l.Items, e => e.Id == admin.Empresa.Id);
        var lista = await (await super.GetAsync($"/api/v1/empresas?texto={admin.Empresa.Nit}")).LeerAsync<ListaPaginada<EmpresaDto>>();
        Assert.Equal("PRUEBA_POR_VENCER", Fila(lista).Membresia!.Situacion);

        // Al día siguiente, sin pago: Gratis automático, sin decisión, y se conserva el vehículo más antiguo.
        await VencerAsync(admin.Empresa.Id);
        var vencida = await (await c.GetAsync("/api/v1/suscripcion")).LeerAsync<SuscripcionDto>();
        Assert.Equal((PlanSuscripcion.Gratis, EstadoSuscripcion.Gratis, "PROFESIONAL"), (vencida.Plan, vencida.Estado, vencida.PlanVencido));
        Assert.NotNull(vencida.FechaVencimiento);
        Assert.Equal(1, vencida.Uso.Vehiculos);
        var activos = await (await c.GetAsync("/api/v1/vehiculos?estado=ACTIVO")).LeerAsync<ListaPaginada<VehiculoDto>>();
        Assert.Equal([vehiculos[0].Id], activos.Items.Select(v => v.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsync("/api/v1/suscripcion/continuar-gratis", null)).StatusCode);

        // Para la plataforma ya está en Gratis (no hay "vencidas").
        lista = await (await super.GetAsync($"/api/v1/empresas?texto={admin.Empresa.Nit}")).LeerAsync<ListaPaginada<EmpresaDto>>();
        Assert.Equal(("GRATIS", "PROFESIONAL"), (Fila(lista).Membresia!.Situacion, Fila(lista).Membresia!.PlanVencido));

        // Para activar otro primero se inactiva el activo.
        object Con(VehiculoDto v, string estado) => new
        {
            v.Placa, v.NumeroInterno, tipo = "BUSETA", v.Marca, v.Linea, v.Modelo, v.Color, v.Capacidad, v.VencimientoSoat,
            v.VencimientoTecnomecanica, v.VencimientoTarjetaOperacion, v.VencimientoPolizas, estado, observaciones = "",
        };
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await c.PutAsJsonAsync($"/api/v1/vehiculos/{vehiculos[1].Id}", Con(vehiculos[1], "ACTIVO"), ClienteApi.Json)).StatusCode);
        await (await c.PutAsJsonAsync($"/api/v1/vehiculos/{vehiculos[0].Id}", Con(vehiculos[0], "INACTIVO"), ClienteApi.Json)).LeerAsync<VehiculoDto>();
        await (await c.PutAsJsonAsync($"/api/v1/vehiculos/{vehiculos[1].Id}", Con(vehiculos[1], "ACTIVO"), ClienteApi.Json)).LeerAsync<VehiculoDto>();

        lista = await (await super.GetAsync($"/api/v1/empresas?texto={admin.Empresa.Nit}")).LeerAsync<ListaPaginada<EmpresaDto>>();
        Assert.Equal(("GRATIS", PlanSuscripcion.Gratis), (Fila(lista).Membresia!.Situacion, Fila(lista).Membresia!.Plan));
    }

    [Fact]
    public async Task La_plataforma_al_bajar_de_plan_inactiva_lo_que_supera_el_limite()
    {
        var admin = await bd.AdminNuevoAsync();
        for (var i = 1; i <= 2; i++)
        {
            await admin.Cliente.CrearAsync<VehiculoDto>("vehiculos", DatosCatalogos.Vehiculo(DatosOperacion.Placa(), $"{i}"));
        }
        var super = await bd.SuperAdminAsync();
        var s = await (await super.PutAsJsonAsync($"/api/v1/empresas/{admin.Empresa.Id}/suscripcion",
            new { plan = "GRATIS", estado = "GRATIS" }, ClienteApi.Json)).LeerAsync<SuscripcionDto>();
        Assert.Equal(1, s.Uso.Vehiculos);
    }

    [Fact]
    public async Task Las_funciones_dependen_del_plan()
    {
        var admin = await bd.AdminNuevoAsync();
        var c = admin.Cliente;
        // En la prueba (Profesional) todo está disponible.
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/v1/reportes/morosos")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/v1/recorridos/en-curso")).StatusCode);

        await VencerAsync(admin.Empresa.Id);
        var mensaje = await (await c.GetAsync("/api/v1/reportes/morosos")).MensajeErrorAsync(HttpStatusCode.Forbidden);
        Assert.Contains("Tu plan Gratis no incluye los reportes financieros", mensaje);
        Assert.Contains("plan Esencial", mensaje);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/v1/recorridos/en-curso")).StatusCode);
        // Programaciones: se pueden ver, no crear.
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/v1/programaciones")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/v1/programaciones", new { }, ClienteApi.Json)).StatusCode);

        // Esencial: reportes sí, GPS no.
        var super = await bd.SuperAdminAsync();
        await (await super.PutAsJsonAsync($"/api/v1/empresas/{admin.Empresa.Id}/suscripcion",
            new { plan = "ESENCIAL", estado = "ACTIVA", periodo = "MENSUAL", fechaFin = DateTime.UtcNow.AddDays(30) }, ClienteApi.Json)).LeerAsync<SuscripcionDto>();
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/v1/reportes/morosos")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/v1/recorridos/en-curso")).StatusCode);
    }

    [Fact]
    public async Task Pagar_de_una_vez_registra_sin_prueba_en_Gratis()
    {
        var usuario = ClienteApi.Unico("pag");
        var login = await (await bd.Api.CreateClient().PostAsJsonAsync("/api/v1/auth/registro", new
        {
            empresa = new { nombre = "Rutas Ya", nit = ClienteApi.NitUnico(), ciudad = "Neiva", telefono = "3001112233" },
            administrador = new { nombre = "Ana Gil", usuario, email = $"{usuario}@correo.co", contrasena = "Clave-123" },
            aceptaTerminos = true,
            pagarAhora = true,
        }, ClienteApi.Json)).LeerAsync<RespuestaLogin>(HttpStatusCode.Created);
        var s = await (await bd.Cliente(login.Tokens.AccessToken).GetAsync("/api/v1/suscripcion")).LeerAsync<SuscripcionDto>();
        Assert.Equal((PlanSuscripcion.Gratis, true, "", 0), (s.Plan, s.PruebaUsada, s.PlanVencido, s.DiasPrueba));
        Assert.Equal(TemaApariencia.Oscuro, login.Sesion.Empresa!.Tema);
    }

    [Fact]
    public async Task Registrarse_en_Gratis_no_vence_y_puede_iniciar_la_prueba_una_vez()
    {
        var usuario = ClienteApi.Unico("gra");
        var nit = ClienteApi.NitUnico();
        var login = await (await bd.Api.CreateClient().PostAsJsonAsync("/api/v1/auth/registro", new
        {
            empresa = new { nombre = "Rutas Libres", nit, ciudad = "Tunja", telefono = "3001112244" },
            administrador = new { nombre = "Luis Mora", usuario, email = $"{usuario}@correo.co", contrasena = "Clave-123" },
            aceptaTerminos = true,
            comienzo = "GRATIS",
        }, ClienteApi.Json)).LeerAsync<RespuestaLogin>(HttpStatusCode.Created);
        var c = bd.Cliente(login.Tokens.AccessToken);

        var s = await (await c.GetAsync("/api/v1/suscripcion")).LeerAsync<SuscripcionDto>();
        Assert.Equal((PlanSuscripcion.Gratis, EstadoSuscripcion.Gratis, false, 0), (s.Plan, s.Estado, s.PruebaUsada, s.DiasPrueba));
        Assert.Null(s.FechaFin);
        Assert.Null(s.DiasRestantes);
        var super = await bd.SuperAdminAsync();
        var empresa = (await (await super.GetAsync($"/api/v1/empresas?texto={nit}")).LeerAsync<ListaPaginada<EmpresaDto>>())
            .Items.Single(e => e.Id == login.Sesion.Empresa!.Id);
        Assert.Equal("GRATIS", empresa.Membresia!.Situacion);

        // Quiere probar: días de Profesional; la segunda vez ya no.
        var prueba = await (await c.PostAsync("/api/v1/suscripcion/prueba", null)).LeerAsync<SuscripcionDto>();
        Assert.Equal((PlanSuscripcion.Profesional, EstadoSuscripcion.Prueba, true, 15), (prueba.Plan, prueba.Estado, prueba.PruebaUsada, prueba.DiasPrueba));
        Assert.Equal(15, prueba.DiasRestantes);
        Assert.Contains("ya tiene un plan", await (await c.PostAsync("/api/v1/suscripcion/prueba", null)).MensajeErrorAsync(HttpStatusCode.UnprocessableEntity));

        // Al vencer sin pago vuelve a Gratis y no puede repetir la prueba.
        await VencerAsync(login.Sesion.Empresa!.Id);
        var vencida = await (await c.GetAsync("/api/v1/suscripcion")).LeerAsync<SuscripcionDto>();
        Assert.Equal((PlanSuscripcion.Gratis, "PROFESIONAL"), (vencida.Plan, vencida.PlanVencido));
        Assert.Contains("ya usó la prueba", await (await c.PostAsync("/api/v1/suscripcion/prueba", null)).MensajeErrorAsync(HttpStatusCode.UnprocessableEntity));
    }

    [Fact]
    public async Task Avisa_a_todos_los_administradores_antes_de_vencer_y_al_pasar_a_Gratis()
    {
        var super = await bd.SuperAdminAsync();
        const string UrlAviso = "/api/v1/plataforma/parametros/suscripcion.dias_aviso_vencimiento";
        await (await super.PutAsJsonAsync(UrlAviso, new { valor = "7" }, ClienteApi.Json)).LeerAsync<ParametroPlataformaDto>();
        try
        {
            var admin = await bd.AdminNuevoAsync();
            var usuarioOtro = ClienteApi.Unico("adm");
            await (await super.PostAsJsonAsync($"/api/v1/empresas/{admin.Empresa.Id}/administradores", new
            {
                nombre = "Otra Administradora", usuario = usuarioOtro, email = $"{usuarioOtro}@correo.co", telefono = "", estado = "ACTIVO", contrasena = "Clave-123",
            }, ClienteApi.Json)).LeerAsync(HttpStatusCode.Created);
            var otroCliente = await bd.ClienteDeAsync(usuarioOtro, "Clave-123");

            // Faltan 10 días: todavía no. Faltan 7: avisa una sola vez.
            await FinEnAsync(admin.Empresa.Id, 10);
            Assert.DoesNotContain(await NotificacionesAsync(admin.Empresa.Id, admin.Cliente), n => n.Clave.StartsWith("membresia:"));
            await FinEnAsync(admin.Empresa.Id, 7);
            var porVencer = Assert.Single(await NotificacionesAsync(admin.Empresa.Id, admin.Cliente), n => n.Clave.StartsWith("membresia:"));
            Assert.Equal((NivelNotificacion.Advertencia, "/sistema/membership"), (porVencer.Nivel, porVencer.Enlace));
            Assert.Contains("vence en 7 días", porVencer.Titulo);
            Assert.Contains("plan Gratis", porVencer.Mensaje);
            Assert.Single(await NotificacionesAsync(admin.Empresa.Id, admin.Cliente), n => n.Clave.StartsWith("membresia:"));

            // Último día y vencida → Gratis (también para el otro administrador).
            await FinEnAsync(admin.Empresa.Id, 0);
            Assert.Contains(await NotificacionesAsync(admin.Empresa.Id, admin.Cliente), n => n.Clave.EndsWith(":ultimo-dia") && n.Nivel == NivelNotificacion.Critica);
            await VencerAsync(admin.Empresa.Id);
            await NotificacionesAsync(admin.Empresa.Id, admin.Cliente);
            Assert.Equal(PlanSuscripcion.Gratis, (await (await admin.Cliente.GetAsync("/api/v1/suscripcion")).LeerAsync<SuscripcionDto>()).Plan);
            foreach (var cliente in new[] { admin.Cliente, otroCliente })
            {
                var lista = await (await cliente.GetAsync("/api/v1/notificaciones?tamano=100")).LeerAsync<ListaPaginada<NotificacionDto>>();
                Assert.Contains(lista.Items, n => n.Clave.StartsWith("membresia:vencida:") && n.Titulo.Contains("ahora estás en Gratis"));
            }
        }
        finally
        {
            await (await super.PutAsJsonAsync(UrlAviso, new { valor = "5" }, ClienteApi.Json)).LeerAsync<ParametroPlataformaDto>();
        }
    }

    [Fact]
    public async Task La_plataforma_fija_los_precios_y_los_pagos_manuales_cuentan_como_ingresos()
    {
        var super = await bd.SuperAdminAsync();
        const string UrlPrecio = "/api/v1/plataforma/parametros/plan.esencial.precio_mensual";
        var parametros = await (await super.GetAsync("/api/v1/plataforma/parametros")).LeerAsync<List<ParametroPlataformaDto>>();
        Assert.Contains(parametros, p => p.Clave == "plan.esencial.precio_mensual" && p is { Tipo: "PESOS", Grupo: "PRECIOS", Valor: "89900" });
        await (await super.PutAsJsonAsync(UrlPrecio, new { valor = "99900" }, ClienteApi.Json)).LeerAsync<ParametroPlataformaDto>();
        try
        {
            var planes = await (await bd.Api.CreateClient().GetAsync("/api/v1/suscripcion/planes")).LeerAsync<List<PlanDto>>();
            Assert.Equal((99_900, 999_000), (planes.Single(p => p.Id == PlanSuscripcion.Esencial).PrecioMensual, planes.Single(p => p.Id == PlanSuscripcion.Esencial).PrecioAnual));
            Assert.Equal(HttpStatusCode.BadRequest, (await super.PutAsJsonAsync(UrlPrecio, new { valor = "10" }, ClienteApi.Json)).StatusCode);

            // Activación manual con el valor recibido por transferencia.
            var admin = await bd.AdminNuevoAsync();
            var hoy = Fechas.Hoy(TimeProvider.System);
            var antes = await (await super.GetAsync($"/api/v1/plataforma/ingresos?desde={hoy:yyyy-MM-dd}&hasta={hoy:yyyy-MM-dd}")).LeerAsync<IngresosPlataformaDto>();
            await (await super.PutAsJsonAsync($"/api/v1/empresas/{admin.Empresa.Id}/suscripcion",
                new { plan = "ESENCIAL", estado = "ACTIVA", periodo = "MENSUAL", fechaFin = DateTime.UtcNow.AddDays(30), valorPagado = 99_900 },
                ClienteApi.Json)).LeerAsync<SuscripcionDto>();
            var pagos = await (await super.GetAsync($"/api/v1/empresas/{admin.Empresa.Id}/suscripcion/pagos")).LeerAsync<List<PagoSuscripcionDto>>();
            Assert.Equal((99_900L, EstadoPagoSuscripcion.Aprobado, "MANUAL"), (Assert.Single(pagos).Valor, pagos[0].Estado, pagos[0].MetodoPago));

            var ingresos = await (await super.GetAsync($"/api/v1/plataforma/ingresos?desde={hoy:yyyy-MM-dd}&hasta={hoy:yyyy-MM-dd}")).LeerAsync<IngresosPlataformaDto>();
            Assert.Equal((antes.Total + 99_900, antes.Pagos + 1, "DIA"), (ingresos.Total, ingresos.Pagos, ingresos.Agrupacion));
            Assert.Equal(ingresos.Total, Assert.Single(ingresos.Serie).Total);
            Assert.Contains(ingresos.PorMetodo, g => g.Clave == "MANUAL");
            Assert.Contains(ingresos.Detalle, p => p.EmpresaId == admin.Empresa.Id && p.Valor == 99_900);
            Assert.True(ingresos.RecurrenteMensual >= 99_900);

            // Rangos largos se agrupan por mes; fechas al revés → 422; solo la plataforma.
            var anual = await (await super.GetAsync($"/api/v1/plataforma/ingresos?desde={hoy.AddMonths(-11):yyyy-MM-dd}&hasta={hoy:yyyy-MM-dd}")).LeerAsync<IngresosPlataformaDto>();
            Assert.Equal("MES", anual.Agrupacion);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await super.GetAsync($"/api/v1/plataforma/ingresos?desde={hoy:yyyy-MM-dd}&hasta={hoy.AddDays(-1):yyyy-MM-dd}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.Cliente.GetAsync("/api/v1/plataforma/ingresos")).StatusCode);
        }
        finally
        {
            await (await super.PutAsJsonAsync(UrlPrecio, new { valor = "89900" }, ClienteApi.Json)).LeerAsync<ParametroPlataformaDto>();
        }
    }
}
