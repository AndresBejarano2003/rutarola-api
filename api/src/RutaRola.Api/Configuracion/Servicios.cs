using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RutaRola.Api.Errores;
using RutaRola.Api.Seguridad;
using RutaRola.Application.Comun;
using RutaRola.Infrastructure;
using RutaRola.Infrastructure.Persistencia;

namespace RutaRola.Api.Configuracion;

public static class Servicios
{
    public const string PoliticaCors = "Frontend";

    public static WebApplicationBuilder AgregarServiciosApi(this WebApplicationBuilder builder)
    {
        var services = builder.Services;

        services.AgregarInfraestructura();
        services.AddHttpContextAccessor();
        services.AddScoped<IContextoUsuario, ContextoUsuarioHttp>();

        services.AddControllers().AddJsonOptions(o => ConfigurarJson(o.JsonSerializerOptions));
        services.ConfigureHttpJsonOptions(o => ConfigurarJson(o.SerializerOptions));
        services.AddRouting(o => o.LowercaseUrls = true);

        services.AddProblemDetails(o => o.CustomizeProblemDetails = DetallesProblema.Completar);
        services.AddExceptionHandler<ManejadorErrores>();

        services.AddCors(o => o.AddPolicy(PoliticaCors, politica => politica
            .WithOrigins(OrigenesCors(builder.Configuration))
            .AllowAnyHeader()
            .AllowAnyMethod()));

        // Detrás del proxy del hosting: IP real del cliente (sesiones, auditoría) y esquema https.
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.KnownIPNetworks.Clear();
            o.KnownProxies.Clear();
        });

        services.AddHealthChecks().AddDbContextCheck<RutaRolaDbContext>("base_datos");

        services.AddOpenApi("v1", o => o.AddDocumentTransformer((documento, _, _) =>
        {
            documento.Info.Title = "RutaRola API";
            documento.Info.Version = "v1";
            documento.Info.Description = "API de RutaRola: transporte escolar multiempresa. Rutas bajo /api/v1.";
            return Task.CompletedTask;
        }));

        return builder;
    }

    /// <summary>
    /// Enums en UPPER_SNAKE (<c>"EN_CURSO"</c>), propiedades en camelCase, fechas <c>YYYY-MM-DD</c>,
    /// horas <c>HH:mm</c>; igual que los tipos del frontend.
    /// </summary>
    public static void ConfigurarJson(JsonSerializerOptions opciones)
    {
        opciones.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        opciones.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper, allowIntegerValues: false));
        opciones.Converters.Add(new ConvertidorHora());
    }

    /// <summary><c>Cors:Origenes</c> como lista (<c>Cors__Origenes__0</c>…) o como texto separado por comas.</summary>
    public static string[] OrigenesCors(IConfiguration configuracion)
    {
        var seccion = configuracion.GetSection("Cors:Origenes");
        var valores = seccion.Value is { } texto ? texto.Split(',') : seccion.Get<string[]>() ?? [];
        return valores.Select(o => o.Trim().TrimEnd('/')).Where(o => o.Length > 0).Distinct().ToArray();
    }

    public static WebApplication UsarPipelineApi(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseSerilogRequestLoggingRutaRola();
        app.UseCors(PoliticaCors);

        if (!app.Environment.IsProduction())
        {
            app.MapOpenApi();
            app.MapScalarApiReferenceRutaRola();
        }

        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = EscribirSalud,
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status200OK,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
            },
        });
        app.MapControllers();
        return app;
    }

    private static Task EscribirSalud(HttpContext http, HealthReport reporte) =>
        http.Response.WriteAsJsonAsync(new
        {
            estado = reporte.Status.ToString(),
            duracionMs = (int)reporte.TotalDuration.TotalMilliseconds,
            componentes = reporte.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString()),
        });
}
