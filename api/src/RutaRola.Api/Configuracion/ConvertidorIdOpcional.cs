using System.Text.Json;
using System.Text.Json.Serialization;

namespace RutaRola.Api.Configuracion;

/// <summary>Ids opcionales: los formularios envían <c>""</c> cuando no hay (p. ej. asignación sin monitora).</summary>
public class ConvertidorIdOpcional : JsonConverter<Guid?>
{
    public override Guid? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var texto = reader.GetString();
        if (string.IsNullOrWhiteSpace(texto))
        {
            return null;
        }
        return Guid.TryParse(texto, out var id) ? id : throw new JsonException("El id no es válido.");
    }

    public override void Write(Utf8JsonWriter writer, Guid? value, JsonSerializerOptions options)
    {
        if (value is { } id)
        {
            writer.WriteStringValue(id);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
