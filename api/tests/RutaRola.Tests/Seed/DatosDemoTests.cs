using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RutaRola.Domain.Enums;
using RutaRola.Infrastructure.Persistencia.Seed;
using RutaRola.Tests.Infraestructura;

namespace RutaRola.Tests.Seed;

/// <summary>El seed demo (solo desarrollo) sobre la empresa demo de la BD de pruebas.</summary>
[Collection(ColeccionBaseDatos.Nombre)]
public class DatosDemoTests(BaseDatosPrueba bd)
{
    [Fact]
    public async Task Crea_los_datos_del_modo_local_una_sola_vez_con_fechas_historicas()
    {
        await using var api = bd.Api.WithWebHostBuilder(b => b
            .UseSetting("Seed:Demo:Habilitado", "true")
            .UseSetting("Seed:Demo:Password", "Demo-Prueba-123"));
        async Task SembrarAsync()
        {
            await using var scope = api.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<SembradorDatos>().SembrarAsync();
        }
        await SembrarAsync();
        await SembrarAsync();

        await using var db = bd.Contexto(null, modoSistema: true);
        var empresa = await db.Empresas.SingleAsync(e => e.Nit == "900000001-1");
        var anio = DatosOperacion.Hoy.Year;
        Assert.Equal(5, await db.Colegios.CountAsync(c => c.IdEmpresa == empresa.Id));
        Assert.Equal(12, await db.Estudiantes.CountAsync(e => e.IdEmpresa == empresa.Id));
        Assert.Equal(4, await db.Rutas.CountAsync(r => r.IdEmpresa == empresa.Id));
        var facturas = await db.Facturas.Where(f => f.IdEmpresa == empresa.Id).ToListAsync();
        Assert.Equal(6, facturas.Count);
        Assert.All(facturas, f => Assert.Equal(new DateOnly(anio, 1, 25), DateOnly.FromDateTime(f.FechaCreacion.AddHours(-5))));
        var recibos = await db.Pagos.Where(p => p.IdEmpresa == empresa.Id).ToListAsync();
        Assert.InRange(recibos.Count, 0, 48); // Ninguno en enero: el primer mes cobrado es febrero.
        Assert.All(recibos, r => Assert.Equal(r.FechaPago, DateOnly.FromDateTime(r.FechaCreacion.AddHours(-5))));
        Assert.Equal(4, await db.Cotizaciones.CountAsync(c => c.IdEmpresa == empresa.Id));
        Assert.Equal(3, await db.ProgramacionesCobro.CountAsync(p => p.IdEmpresa == empresa.Id));
        Assert.Equal(
            await db.EnviosProgramacion.CountAsync(e => e.Programacion.IdEmpresa == empresa.Id && e.Estado == EstadoEnvio.Fallido) > 0,
            DatosOperacion.Hoy >= new DateOnly(anio, 9, 5));
        Assert.All(await db.Estudiantes.Where(e => e.IdEmpresa == empresa.Id).ToListAsync(),
            e => Assert.Equal(new DateOnly(anio, 1, 15), DateOnly.FromDateTime(e.FechaCreacion.AddHours(-5))));
    }
}
