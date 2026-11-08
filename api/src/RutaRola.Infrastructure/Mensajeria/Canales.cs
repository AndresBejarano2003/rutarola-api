using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RutaRola.Application.Comunicacion;

namespace RutaRola.Infrastructure.Mensajeria;

/// <summary>
/// Modo de un canal (<c>Mensajeria:Whatsapp</c>, <c>Mensajeria:Correo</c>):
/// <c>Deshabilitado</c> (por defecto: no se envía ni se registra nada), <c>Simulado</c> (desarrollo:
/// registra el envío como entregado sin salir del servidor) y, solo para el correo, <c>Brevo</c> (API HTTPS
/// de Brevo, sirve en hostings que bloquean SMTP como el plan gratuito de Render) o <c>Smtp</c>.
/// WhatsApp Business se conecta implementando <see cref="ICanalWhatsapp"/>.
/// </summary>
public static class ModoCanal
{
    public const string Deshabilitado = "Deshabilitado";
    public const string Simulado = "Simulado";
    public const string Brevo = "Brevo";
    public const string Smtp = "Smtp";

    public static string Leer(IConfiguration configuracion, string canal, params string[] reales)
    {
        var valor = configuracion[$"Mensajeria:{canal}"];
        if (string.IsNullOrWhiteSpace(valor)) return Deshabilitado;
        var validos = new[] { Deshabilitado, Simulado }.Concat(reales).ToArray();
        return validos.FirstOrDefault(v => v.Equals(valor, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Mensajeria:{canal} = '{valor}' no es válido. Usa {string.Join(", ", validos.Select(v => $"'{v}'"))}.");
    }
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

/// <summary>
/// Correo: deshabilitado, simulado, Brevo (<c>Correo:Brevo:ApiKey</c>) o SMTP (<c>Correo:Smtp:*</c>, STARTTLS en
/// el 587). Remitente: <c>Correo:Remitente</c> (verificado en el proveedor) y <c>Correo:NombreRemitente</c>.
/// Si falta la configuración del proveedor elegido, el canal queda deshabilitado y lo dice en el log al arrancar.
/// Nunca registra el destinatario ni el texto.
/// </summary>
internal sealed class CanalCorreoServidor : ICanalCorreo, IDisposable
{
    private const string UrlBrevo = "https://api.brevo.com/v3/smtp/email";

    private readonly IConfiguration configuracion;
    private readonly ILogger<CanalCorreoServidor> logger;
    private readonly string modo;
    private readonly string remitente;
    private readonly string nombreRemitente;
    /// <summary>Un solo cliente para toda la vida del proceso (el canal es singleton).</summary>
    private readonly HttpClient http = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) })
    {
        Timeout = TimeSpan.FromSeconds(20),
    };

    public CanalCorreoServidor(IConfiguration configuracion, ILogger<CanalCorreoServidor> logger)
    {
        this.configuracion = configuracion;
        this.logger = logger;
        modo = ModoCanal.Leer(configuracion, "Correo", ModoCanal.Brevo, ModoCanal.Smtp);
        remitente = configuracion["Correo:Remitente"] ?? "";
        nombreRemitente = configuracion["Correo:NombreRemitente"] is { Length: > 0 } n ? n : "RutaRola";
        var falta = modo switch
        {
            ModoCanal.Brevo when string.IsNullOrWhiteSpace(configuracion["Correo:Brevo:ApiKey"]) => "Correo:Brevo:ApiKey",
            ModoCanal.Smtp when string.IsNullOrWhiteSpace(configuracion["Correo:Smtp:Servidor"]) => "Correo:Smtp:Servidor",
            ModoCanal.Brevo or ModoCanal.Smtp when string.IsNullOrWhiteSpace(remitente) => "Correo:Remitente",
            _ => null,
        };
        if (falta is not null)
        {
            logger.LogError("Correo {Modo} sin configurar ({Falta}): no se enviarán correos", modo, falta);
            modo = ModoCanal.Deshabilitado;
        }
        Habilitado = modo != ModoCanal.Deshabilitado;
    }

    public bool Habilitado { get; }

    public async Task<ResultadoEnvio> EnviarAsync(
        string destinatario, string asunto, string cuerpo, IReadOnlyList<AdjuntoCorreo> adjuntos, CancellationToken ct)
    {
        switch (modo)
        {
            case ModoCanal.Brevo:
                return await EnviarBrevoAsync(destinatario, asunto, cuerpo, adjuntos, ct);
            case ModoCanal.Smtp:
                return await EnviarSmtpAsync(destinatario, asunto, cuerpo, adjuntos, ct);
            default:
                logger.LogInformation("Correo simulado con {Adjuntos} adjunto(s)", adjuntos.Count);
                return new ResultadoEnvio(true, "Entregado (simulado, sin proveedor configurado)");
        }
    }

    private async Task<ResultadoEnvio> EnviarBrevoAsync(
        string destinatario, string asunto, string cuerpo, IReadOnlyList<AdjuntoCorreo> adjuntos, CancellationToken ct)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Post, UrlBrevo)
        {
            Content = System.Net.Http.Json.JsonContent.Create(new
            {
                sender = new { name = nombreRemitente, email = remitente },
                to = new[] { new { email = destinatario } },
                subject = asunto,
                textContent = cuerpo,
                attachment = adjuntos.Count == 0 ? null : adjuntos.Select(a => new { name = a.Nombre, content = Convert.ToBase64String(a.Contenido) }).ToArray(),
            }, options: new System.Text.Json.JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }),
        };
        peticion.Headers.Add("api-key", configuracion["Correo:Brevo:ApiKey"]);
        peticion.Headers.Accept.ParseAdd("application/json");
        using var respuesta = await http.SendAsync(peticion, ct);
        if (respuesta.IsSuccessStatusCode)
        {
            return new ResultadoEnvio(true, "Entregado a Brevo");
        }
        // El cuerpo de error de Brevo no trae el destinatario ni el texto.
        var detalle = await respuesta.Content.ReadAsStringAsync(ct);
        return new ResultadoEnvio(false, $"Brevo respondió {(int)respuesta.StatusCode}: {(detalle.Length > 300 ? detalle[..300] : detalle)}");
    }

    private async Task<ResultadoEnvio> EnviarSmtpAsync(
        string destinatario, string asunto, string cuerpo, IReadOnlyList<AdjuntoCorreo> adjuntos, CancellationToken ct)
    {
        using var mensaje = new System.Net.Mail.MailMessage
        {
            From = new System.Net.Mail.MailAddress(remitente, nombreRemitente),
            Subject = asunto,
            Body = cuerpo,
            IsBodyHtml = false,
        };
        mensaje.To.Add(destinatario);
        var flujos = new List<MemoryStream>();
        try
        {
            foreach (var a in adjuntos)
            {
                var flujo = new MemoryStream(a.Contenido);
                flujos.Add(flujo);
                mensaje.Attachments.Add(new System.Net.Mail.Attachment(flujo, a.Nombre, a.Tipo));
            }
            using var cliente = new System.Net.Mail.SmtpClient(configuracion["Correo:Smtp:Servidor"], configuracion.GetValue("Correo:Smtp:Puerto", 587))
            {
                EnableSsl = configuracion.GetValue("Correo:Smtp:Ssl", true),
                Credentials = string.IsNullOrEmpty(configuracion["Correo:Smtp:Usuario"])
                    ? null
                    : new System.Net.NetworkCredential(configuracion["Correo:Smtp:Usuario"], configuracion["Correo:Smtp:Clave"]),
                Timeout = 20_000,
            };
            await cliente.SendMailAsync(mensaje, ct);
            return new ResultadoEnvio(true, "Entregado al servidor SMTP");
        }
        catch (System.Net.Mail.SmtpException ex)
        {
            return new ResultadoEnvio(false, $"SMTP: {ex.StatusCode}");
        }
        finally
        {
            foreach (var f in flujos) f.Dispose();
        }
    }

    public void Dispose() => http.Dispose();
}
