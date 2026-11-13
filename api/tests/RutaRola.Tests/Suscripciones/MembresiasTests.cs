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

    private async Task VencerAsync(Guid idEmpresa)
    {
        await using var db = bd.Contexto(null, modoSistema: true);
        await db.Suscripciones.IgnoreQueryFilters().Where(s => s.IdEmpresa == idEmpresa)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.FechaFin, DateTime.UtcNow.AddMinutes(-1)));
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
    public async Task Al_vencer_pide_decidir_y_seguir_en_Gratis_inactiva_lo_que_sobra()
    {
        var admin = await bd.AdminNuevoAsync();
        var c = admin.Cliente;
        var vehiculos = new List<VehiculoDto>();
        for (var i = 1; i <= 3; i++)
        {
            vehiculos.Add(await c.CrearAsync<VehiculoDto>("vehiculos", DatosCatalogos.Vehiculo(DatosOperacion.Placa(), $"{i}")));
        }
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PostAsync("/api/v1/suscripcion/continuar-gratis", null)).StatusCode);

        await VencerAsync(admin.Empresa.Id);
        var vencida = await (await c.GetAsync("/api/v1/suscripcion")).LeerAsync<SuscripcionDto>();
        Assert.Equal((PlanSuscripcion.Gratis, true, "PROFESIONAL"), (vencida.Plan, vencida.PendienteDecision, vencida.PlanVencido));
        Assert.NotNull(vencida.FechaVencimiento);

        // La plataforma la ve vencida (sin decidir) en su tabla y en el resumen.
        var super = await bd.SuperAdminAsync();
        EmpresaDto Fila(ListaPaginada<EmpresaDto> l) => Assert.Single(l.Items, e => e.Id == admin.Empresa.Id);
        var lista = await (await super.GetAsync($"/api/v1/empresas?texto={admin.Empresa.Nit}")).LeerAsync<ListaPaginada<EmpresaDto>>();
        Assert.Equal(("VENCIDA", "PROFESIONAL"), (Fila(lista).Membresia!.Situacion, Fila(lista).Membresia!.PlanVencido));
        var vencidas = await (await super.GetAsync("/api/v1/empresas?membresia=VENCIDA&tamano=1000")).LeerAsync<ListaPaginada<EmpresaDto>>();
        Assert.Contains(vencidas.Items, e => e.Id == admin.Empresa.Id);
        Assert.True((await (await super.GetAsync("/api/v1/empresas/resumen-membresias")).LeerAsync<ResumenMembresiasDto>()).Vencida >= 1);

        var decision = await (await c.PostAsync("/api/v1/suscripcion/continuar-gratis", null)).LeerAsync<DecisionGratisDto>();
        Assert.False(decision.Suscripcion.PendienteDecision);
        Assert.Equal(2, decision.Inactivados.Vehiculos.Count);
        Assert.DoesNotContain(vehiculos[0].Placa, decision.Inactivados.Vehiculos); // se conserva el más antiguo
        var activos = await (await c.GetAsync("/api/v1/vehiculos?estado=ACTIVO")).LeerAsync<ListaPaginada<VehiculoDto>>();
        Assert.Equal([vehiculos[0].Id], activos.Items.Select(v => v.Id));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PostAsync("/api/v1/suscripcion/continuar-gratis", null)).StatusCode);

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
        Assert.Equal((1, false), (s.Uso.Vehiculos, s.PendienteDecision));
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
        await (await c.PostAsync("/api/v1/suscripcion/continuar-gratis", null)).LeerAsync<DecisionGratisDto>();
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
    public async Task Pagar_de_una_vez_registra_sin_prueba_y_con_la_pasarela_pendiente()
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
        Assert.Equal((PlanSuscripcion.Gratis, true, "", 0), (s.Plan, s.PendienteDecision, s.PlanVencido, s.DiasPrueba));
        Assert.Equal(TemaApariencia.Oscuro, login.Sesion.Empresa!.Tema);
    }
}
