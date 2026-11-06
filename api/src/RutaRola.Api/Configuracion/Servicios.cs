using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RutaRola.Api.Errores;
using RutaRola.Api.Seguridad;
using RutaRola.Application;
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

        services.AgregarAplicacion();
        services.AgregarInfraestructura();
        services.AddHttpContextAccessor();
        services.AddScoped<IContextoUsuario, ContextoUsuarioHttp>();
        services.AgregarSeguridad(builder.Configuration);

        services.AddControllers(o =>
            {
                o.Filters.Add<FiltroValidacion>();
                // Los campos obligatorios los valida FluentValidation (mensajes en español), no el model binding.
                o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
            })
            .AddJsonOptions(o => ConfigurarJson(o.JsonSerializerOptions));
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

        services.AddOpenApi("v1", o => o.AddDocumentTransformer<DocumentoOpenApi>());

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
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseAuthorization();

        if (!app.Environment.IsProduction())
        {
            app.MapOpenApi().AllowAnonymous();
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
        }).AllowAnonymous();
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
