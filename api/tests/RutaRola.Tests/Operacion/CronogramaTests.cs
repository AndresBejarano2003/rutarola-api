using System.Net;
using System.Net.Http.Json;
using RutaRola.Application.Operacion;
using RutaRola.Application.Suscripciones;
using RutaRola.Domain.Enums;
using RutaRola.Tests.Infraestructura;

namespace RutaRola.Tests.Operacion;

/// <summary>Cronograma: rutas programadas por día hábil, recorridos, avisos de ausencia, agenda, plan y aislamiento.</summary>
[Collection(ColeccionBaseDatos.Nombre)]
public class CronogramaTests(BaseDatosPrueba bd)
{
    private static DateOnly ProximoHabil(DateOnly desde)
    {
        var d = desde;
        while (!ServicioCronograma.EsHabil(d)) d = d.AddDays(1);
        return d;
    }

    [Fact]
    public async Task Muestra_rutas_recorridos_y_ausencias_por_dia()
    {
        var e = await bd.EscenarioAsync();
        var c = e.Admin.Cliente;
        var hoy = DatosOperacion.Hoy;
        var habil = ProximoHabil(hoy.AddDays(1));
        var acudiente = await bd.ClienteDeAsync(e.Acudiente.Usuario, "Clave-123");
        await (await acudiente.PostAsJsonAsync("/api/v1/portal/avisos", new
        {
            estudianteId = e.Estudiantes[0].Id, fecha = habil.ToString("yyyy-MM-dd"), motivo = "POR_SU_CUENTA", trayecto = "ENTREGA", nota = "Lo recoge la abuela",
        }, ClienteApi.Json)).LeerAsync(HttpStatusCode.Created);
        var recorrido = await (await e.ClienteConductor.IniciarAsync(e.Asignacion.Id)).LeerAsync<RecorridoDto>(HttpStatusCode.Created);

        var semana = await (await c.GetAsync($"/api/v1/cronograma?desde={hoy:yyyy-MM-dd}&hasta={hoy.AddDays(13):yyyy-MM-dd}"))
            .LeerAsync<CronogramaDto>();
        Assert.Equal(14, semana.Dias.Count);
        var diaHoy = semana.Dias[0];
        Assert.Equal((1, 1), (diaHoy.Rutas, diaHoy.EnCurso));
        var diaAviso = Assert.Single(semana.Dias, d => d.Fecha == habil);
        Assert.Equal((1, 1), (diaAviso.Rutas, diaAviso.Ausencias));
        Assert.All(semana.Dias.Where(d => !d.Habil && d.Fecha != hoy), d => Assert.Equal(0, d.Rutas));

        var detalle = await (await c.GetAsync($"/api/v1/cronograma/dia?fecha={habil:yyyy-MM-dd}")).LeerAsync<DetalleDiaCronogramaDto>();
        var ruta = Assert.Single(detalle.Rutas);
        Assert.Equal((e.Ruta.Id, 1, e.Vehiculo.Placa), (ruta.RutaId, ruta.Ausencias, ruta.Vehiculo));
        Assert.All(ruta.Trayectos, t => Assert.Equal("PROGRAMADO", t.Estado));
        var ausencia = Assert.Single(detalle.Ausencias);
        Assert.Equal(("ENTREGA", MotivoAusencia.PorSuCuenta, "Lo recoge la abuela"), (ausencia.Trayecto, ausencia.Motivo, ausencia.Nota));
        Assert.Contains(e.Ruta.Codigo, ausencia.Ruta);

        var hoyDetalle = await (await c.GetAsync($"/api/v1/cronograma/dia?fecha={hoy:yyyy-MM-dd}")).LeerAsync<DetalleDiaCronogramaDto>();
        Assert.Equal(("EN_CURSO", recorrido.Id.ToString()),
            (hoyDetalle.Rutas[0].Trayectos.Single(t => t.Trayecto == Trayecto.Recogida).Estado, hoyDetalle.Rutas[0].Trayectos[0].RecorridoId));

        // Otra empresa no ve nada; rango inválido → 422.
        var otra = await bd.AdminNuevoAsync();
        var ajena = await (await otra.Cliente.GetAsync($"/api/v1/cronograma?desde={hoy:yyyy-MM-dd}&hasta={hoy.AddDays(13):yyyy-MM-dd}")).LeerAsync<CronogramaDto>();
        Assert.All(ajena.Dias, d => Assert.Equal((0, 0), (d.Rutas, d.Ausencias)));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.GetAsync($"/api/v1/cronograma?desde={hoy:yyyy-MM-dd}&hasta={hoy.AddDays(500):yyyy-MM-dd}")).StatusCode);
    }

    [Fact]
    public async Task Requiere_el_plan_Profesional()
    {
        var admin = await bd.AdminNuevoAsync();
        var hoy = DatosOperacion.Hoy;
        var url = $"/api/v1/cronograma?desde={hoy:yyyy-MM-dd}&hasta={hoy.AddDays(6):yyyy-MM-dd}";
        Assert.Equal(HttpStatusCode.OK, (await admin.Cliente.GetAsync(url)).StatusCode); // prueba = Profesional
        var super = await bd.SuperAdminAsync();
        await (await super.PutAsJsonAsync($"/api/v1/empresas/{admin.Empresa.Id}/suscripcion",
            new { plan = "ESENCIAL", estado = "ACTIVA", periodo = "MENSUAL", fechaFin = DateTime.UtcNow.AddDays(30) }, ClienteApi.Json)).LeerAsync<SuscripcionDto>();
        var mensaje = await (await admin.Cliente.GetAsync(url)).MensajeErrorAsync(HttpStatusCode.Forbidden);
        Assert.Contains("el cronograma de la operación", mensaje);
        Assert.Contains("plan Profesional", mensaje);
    }
}
