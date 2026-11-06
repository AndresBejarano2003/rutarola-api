using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RutaRola.Api.Configuracion;

/// <summary>
/// Fechas opcionales <c>YYYY-MM-DD</c> como las maneja el frontend: los formularios envían <c>""</c>
/// cuando no hay fecha (p. ej. la licencia de una monitora) y esperan <c>""</c> de vuelta.
/// </summary>
public class ConvertidorFechaOpcional : JsonConverter<DateOnly?>
{
    public override bool HandleNull => true;

    public override DateOnly? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }
        var texto = reader.GetString();
        if (string.IsNullOrWhiteSpace(texto))
        {
            return null;
        }
        return DateOnly.TryParseExact(texto, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha)
            ? fecha
            : throw new JsonException("La fecha debe tener el formato AAAA-MM-DD.");
    }

    public override void Write(Utf8JsonWriter writer, DateOnly? value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "");
}
