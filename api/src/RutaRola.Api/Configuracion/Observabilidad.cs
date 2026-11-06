using Scalar.AspNetCore;
using Serilog;
using Serilog.Formatting.Compact;

namespace RutaRola.Api.Configuracion;

public static class Observabilidad
{
    /// <summary>Serilog a consola: texto legible en desarrollo, JSON compacto (una línea por evento) fuera de él.</summary>
    public static WebApplicationBuilder AgregarSerilog(this WebApplicationBuilder builder)
    {
        builder.Services.AddSerilog((servicios, log) =>
        {
            log.ReadFrom.Configuration(builder.Configuration)
                .ReadFrom.Services(servicios)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Aplicacion", "RutaRola.Api");
            if (builder.Environment.IsDevelopment())
            {
                log.WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");
            }
            else
            {
                log.WriteTo.Console(new RenderedCompactJsonFormatter());
            }
        });
        return builder;
    }

    public static WebApplication UseSerilogRequestLoggingRutaRola(this WebApplication app)
    {
        // No registra query strings ni cuerpos (pueden contener datos de menores).
        app.UseSerilogRequestLogging(o =>
        {
            o.MessageTemplate = "HTTP {RequestMethod} {RequestPath} → {StatusCode} en {Elapsed:0} ms";
            o.GetLevel = (http, _, ex) => ex is not null || http.Response.StatusCode >= 500
                ? Serilog.Events.LogEventLevel.Error
                : http.Request.Path.StartsWithSegments("/health") ? Serilog.Events.LogEventLevel.Verbose : Serilog.Events.LogEventLevel.Information;
        });
        return app;
    }

    /// <summary>Documentación interactiva en <c>/docs</c> (solo fuera de producción).</summary>
    public static WebApplication MapScalarApiReferenceRutaRola(this WebApplication app)
    {
        app.MapScalarApiReference("/docs", o => o.WithTitle("RutaRola API"));
        return app;
    }
}
