using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RutaRola.Api.Configuracion;

/// <summary>
/// Horas opcionales <c>HH:mm</c>: el frontend envía <c>""</c> cuando no hay (p. ej. una asignación sin horario) y
/// espera <c>""</c> de vuelta, como <see cref="ConvertidorFechaOpcional"/>.
/// </summary>
public class ConvertidorHoraOpcional : JsonConverter<TimeOnly?>
{
    public override bool HandleNull => true;

    public override TimeOnly? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
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
        return TimeOnly.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.None, out var hora)
            ? hora
            : throw new JsonException("La hora debe tener el formato HH:mm.");
    }

    public override void Write(Utf8JsonWriter writer, TimeOnly? value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "");
}
