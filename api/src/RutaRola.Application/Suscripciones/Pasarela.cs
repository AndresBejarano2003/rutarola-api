using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RutaRola.Application.Suscripciones;

/// <summary>
/// Sección <c>Wompi</c> (secretos: variables de entorno / user-secrets, nunca en el repositorio). Las llaves
/// <c>pub_test_…</c> usan el sandbox; <c>pub_prod_…</c>, producción. Sin llave pública ni secreto de integridad
/// el pago en línea queda deshabilitado.
/// </summary>
public class OpcionesWompi
{
    public const string Seccion = "Wompi";

    public string LlavePublica { get; set; } = "";
    public string SecretoIntegridad { get; set; } = "";
    public string SecretoEventos { get; set; } = "";
    /// <summary>A dónde vuelve el navegador después de pagar (frontend, sin <c>/#/</c>).</summary>
    public string UrlRetorno { get; set; } = "https://rutarola.netlify.app";

    public bool Habilitada => LlavePublica.Length > 0 && SecretoIntegridad.Length > 0;
    public bool Produccion => LlavePublica.StartsWith("pub_prod_", StringComparison.Ordinal);
    public string Ambiente => Produccion ? "prod" : "test";
    public string UrlApi => Produccion ? "https://production.wompi.co/v1" : "https://sandbox.wompi.co/v1";
}

/// <summary>Una transacción tal como la informa Wompi (evento o consulta).</summary>
public record TransaccionPasarela(
    string Id, string Referencia, long ValorCentavos, string Moneda, string Estado, string? MetodoPago, string? Correo);

/// <summary>Consulta de transacciones en el API de Wompi (implementación HTTP en Infrastructure).</summary>
public interface IPasarelaPagos
{
    /// <summary><c>GET {api}/transactions/{id}</c> (público con la llave pública). null si no existe.</summary>
    Task<TransaccionPasarela?> ConsultarAsync(string idTransaccion, CancellationToken ct);
}

/// <summary>Firmas de Wompi (documentación oficial: widget-checkout-web y eventos).</summary>
public static class FirmasWompi
{
    /// <summary>Firma de integridad del checkout: SHA256(referencia + centavos + moneda + secreto de integridad).</summary>
    public static string Integridad(string referencia, long centavos, string moneda, string secreto) =>
        Sha256($"{referencia}{centavos}{moneda}{secreto}");

    /// <summary>
    /// Verifica un evento: SHA256(valores de <c>signature.properties</c> dentro de <c>data</c>, en orden +
    /// <c>timestamp</c> + secreto de eventos) = <c>signature.checksum</c> (también llega en <c>X-Event-Checksum</c>).
    /// </summary>
    public static bool EventoValido(JsonElement evento, string secretoEventos)
    {
        if (secretoEventos.Length == 0
            || !evento.TryGetProperty("data", out var data)
            || !evento.TryGetProperty("signature", out var firma)
            || !firma.TryGetProperty("properties", out var propiedades) || propiedades.ValueKind != JsonValueKind.Array
            || !firma.TryGetProperty("checksum", out var checksum) || checksum.ValueKind != JsonValueKind.String
            || !evento.TryGetProperty("timestamp", out var marca))
        {
            return false;
        }
        var texto = new StringBuilder();
        foreach (var propiedad in propiedades.EnumerateArray())
        {
            var valor = data;
            foreach (var parte in (propiedad.GetString() ?? "").Split('.'))
            {
                if (valor.ValueKind != JsonValueKind.Object || !valor.TryGetProperty(parte, out valor)) return false;
            }
            texto.Append(valor.ValueKind == JsonValueKind.String ? valor.GetString() : valor.GetRawText());
        }
        texto.Append(marca.ValueKind == JsonValueKind.String ? marca.GetString() : marca.GetRawText());
        texto.Append(secretoEventos);
        var esperado = Encoding.ASCII.GetBytes(Sha256(texto.ToString()));
        var recibido = Encoding.ASCII.GetBytes((checksum.GetString() ?? "").ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(esperado, recibido);
    }

    /// <summary>La transacción de un evento <c>transaction.updated</c>, o null si el evento es de otro tipo.</summary>
    public static TransaccionPasarela? Transaccion(JsonElement evento)
    {
        if (!evento.TryGetProperty("event", out var tipo) || tipo.GetString() != "transaction.updated"
            || !evento.TryGetProperty("data", out var data) || !data.TryGetProperty("transaction", out var t))
        {
            return null;
        }
        return DeJson(t);
    }

    public static TransaccionPasarela? DeJson(JsonElement t)
    {
        string? Texto(string nombre) => t.TryGetProperty(nombre, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        if (Texto("id") is not { } id || Texto("reference") is not { } referencia
            || !t.TryGetProperty("amount_in_cents", out var centavos) || !centavos.TryGetInt64(out var valor))
        {
            return null;
        }
        return new TransaccionPasarela(id, referencia, valor, Texto("currency") ?? "", Texto("status") ?? "", Texto("payment_method_type"), Texto("customer_email"));
    }

    private static string Sha256(string texto) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(texto))).ToLowerInvariant();
}
