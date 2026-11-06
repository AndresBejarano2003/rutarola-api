using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RutaRola.Infrastructure.Persistencia.Seed;

namespace RutaRola.Infrastructure.Persistencia;

public static class BaseDatos
{
    /// <summary>Aplica las migraciones pendientes y siembra los datos base.</summary>
    public static async Task MigrarYSembrarAsync(IServiceProvider servicios, CancellationToken ct = default)
    {
        await using var scope = servicios.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RutaRolaDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(BaseDatos));

        var pendientes = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
        logger.LogInformation("Migraciones pendientes: {Cantidad} {Migraciones}", pendientes.Count, pendientes);
        await db.Database.MigrateAsync(ct);
        await scope.ServiceProvider.GetRequiredService<SembradorDatos>().SembrarAsync(ct);
        logger.LogInformation("Base de datos lista");
    }
}
