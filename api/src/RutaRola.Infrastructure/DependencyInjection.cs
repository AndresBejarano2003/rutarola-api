using RutaRola.Application.Comunicacion;
using RutaRola.Infrastructure.Mensajeria;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RutaRola.Application.Autenticacion;
using RutaRola.Application.Comun;
using RutaRola.Infrastructure.Seguridad;
using RutaRola.Domain.Seguridad;
using RutaRola.Infrastructure.Persistencia;
using RutaRola.Infrastructure.Persistencia.Seed;

namespace RutaRola.Infrastructure;

public static class DependencyInjection
{
    public const string NombreConexion = "RutaRola";
    public const string TablaMigraciones = "__ef_migraciones";

    public static IServiceCollection AgregarInfraestructura(this IServiceCollection services)
    {
        services.AddDbContext<RutaRolaDbContext>((sp, opciones) =>
        {
            var cadena = sp.GetRequiredService<IConfiguration>().GetConnectionString(NombreConexion)
                ?? throw new InvalidOperationException(
                    $"Falta la cadena de conexión 'ConnectionStrings:{NombreConexion}' (variable ConnectionStrings__{NombreConexion} o dotnet user-secrets).");
            Configurar(opciones, cadena);
        });
        services.AddScoped<IBaseDatos>(sp => sp.GetRequiredService<RutaRolaDbContext>());
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IGeneradorTokens, GeneradorTokens>();
        services.AddScoped<IPasswordHasher<Usuario>, PasswordHasher<Usuario>>();
        services.AddScoped<SembradorDatos>();
        services.AddSingleton<ICanalWhatsapp, CanalWhatsappServidor>();
        services.AddSingleton<ICanalCorreo, CanalCorreoServidor>();
        return services;
    }

    public static void Configurar(DbContextOptionsBuilder opciones, string cadena) =>
        opciones
            .UseNpgsql(cadena, npgsql => npgsql.MigrationsHistoryTable(TablaMigraciones).UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .UseSnakeCaseNamingConvention()
            // Las tablas hijas tienen el mismo filtro de empresa que su padre (ver RutaRolaDbContext).
            .ConfigureWarnings(w => w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));
}
