using RutaRola.Api.Configuracion;
using RutaRola.Infrastructure.Persistencia;
using Serilog;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Plataformas de contenedores (Render, Railway, Cloud Run, Fly…) indican el puerto con PORT.
    if (Environment.GetEnvironmentVariable("PORT") is { Length: > 0 } puerto)
    {
        builder.WebHost.UseUrls($"http://0.0.0.0:{puerto}");
    }

    // Datos demo en producción solo con permiso explícito (despliegue de demostración con datos ficticios).
    if (builder.Environment.IsProduction() && builder.Configuration.GetValue<bool>("Seed:Demo:Habilitado")
        && !builder.Configuration.GetValue<bool>("Seed:Demo:PermitirEnProduccion"))
    {
        builder.Configuration["Seed:Demo:Habilitado"] = "false";
        Log.Warning("Seed:Demo:Habilitado se ignora en producción sin Seed:Demo:PermitirEnProduccion = true.");
    }

    builder.AgregarSerilog().AgregarServiciosApi();
    var app = builder.Build();

    // `dotnet RutaRola.Api.dll migrar`: aplica migraciones + datos base y termina (paso explícito del despliegue).
    if (args.Contains("migrar"))
    {
        await BaseDatos.MigrarYSembrarAsync(app.Services);
        return 0;
    }

    if (app.Configuration.GetValue<bool>("BaseDatos:MigrarAlIniciar"))
    {
        if (app.Environment.IsProduction())
        {
            Log.Warning("BaseDatos:MigrarAlIniciar se ignora en producción: usa el comando 'migrar'.");
        }
        else
        {
            await BaseDatos.MigrarYSembrarAsync(app.Services);
        }
    }

    app.UsarPipelineApi();
    await app.RunAsync();
    return 0;
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "El API terminó por un error");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>Visible para las pruebas de integración (WebApplicationFactory).</summary>
public partial class Program;
