using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RutaRola.Api.Configuracion;

/// <summary>Horas como <c>HH:mm</c> (igual que los <c>input type="time"</c> del frontend); acepta también <c>HH:mm:ss</c>.</summary>
public class ConvertidorHora : JsonConverter<TimeOnly>
{
    public override TimeOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        TimeOnly.Parse(reader.GetString() ?? throw new JsonException("Hora vacía"), CultureInfo.InvariantCulture);

    public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString("HH:mm", CultureInfo.InvariantCulture));
}
