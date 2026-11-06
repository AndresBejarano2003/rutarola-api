using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RutaRola.Application.Comunicacion;

namespace RutaRola.Infrastructure.Mensajeria;

/// <summary>
/// Modo de un canal (<c>Mensajeria:Whatsapp</c>, <c>Mensajeria:Correo</c>):
/// <c>Deshabilitado</c> (por defecto: no se envía ni se registra nada) o <c>Simulado</c> (desarrollo:
/// registra el envío como entregado sin salir del servidor). El proveedor real (WhatsApp Business,
/// SMTP o un servicio de correo) se conecta implementando <see cref="ICanalWhatsapp"/> / <see cref="ICanalCorreo"/>.
/// </summary>
public static class ModoCanal
{
    public const string Deshabilitado = "Deshabilitado";
    public const string Simulado = "Simulado";

    public static string Leer(IConfiguration configuracion, string canal) => configuracion[$"Mensajeria:{canal}"] switch
    {
        null or "" => Deshabilitado,
        var modo when modo.Equals(Simulado, StringComparison.OrdinalIgnoreCase) => Simulado,
        var modo when modo.Equals(Deshabilitado, StringComparison.OrdinalIgnoreCase) => Deshabilitado,
        var modo => throw new InvalidOperationException(
            $"Mensajeria:{canal} = '{modo}' no es válido. Usa '{Deshabilitado}' o '{Simulado}' hasta conectar un proveedor."),
    };
}

/// <summary>WhatsApp simulado o deshabilitado. Nunca registra el texto (lleva datos de menores).</summary>
internal sealed class CanalWhatsappServidor(IConfiguration configuracion, ILogger<CanalWhatsappServidor> logger) : ICanalWhatsapp
{
    public bool Habilitado { get; } = ModoCanal.Leer(configuracion, "Whatsapp") == ModoCanal.Simulado;

    public Task<ResultadoEnvio> EnviarAsync(string telefono, string mensaje, CancellationToken ct)
    {
        logger.LogInformation("WhatsApp simulado: mensaje de {Caracteres} caracteres", mensaje.Length);
        return Task.FromResult(new ResultadoEnvio(true, "Entregado (simulado, sin proveedor configurado)", $"simulado-{Guid.CreateVersion7()}"));
    }
}

/// <summary>Correo simulado o deshabilitado. Nunca registra el destinatario ni el texto.</summary>
internal sealed class CanalCorreoServidor(IConfiguration configuracion, ILogger<CanalCorreoServidor> logger) : ICanalCorreo
{
    public bool Habilitado { get; } = ModoCanal.Leer(configuracion, "Correo") == ModoCanal.Simulado;

    public Task<ResultadoEnvio> EnviarAsync(
        string destinatario, string asunto, string cuerpo, IReadOnlyList<AdjuntoCorreo> adjuntos, CancellationToken ct)
    {
        logger.LogInformation("Correo simulado con {Adjuntos} adjunto(s)", adjuntos.Count);
        return Task.FromResult(new ResultadoEnvio(true, "Entregado (simulado, sin proveedor configurado)"));
    }
}
