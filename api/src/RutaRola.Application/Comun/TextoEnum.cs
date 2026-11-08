namespace RutaRola.Application.Comun;

/// <summary>Filtros de enums que llegan en la query como <c>UPPER_SNAKE</c> (<c>EN_CURSO</c> → <c>EnCurso</c>).</summary>
public static class TextoEnum
{
    public static T? Leer<T>(string? texto) where T : struct, Enum =>
        Texto.Opcional(texto) is { } valor && Enum.TryParse<T>(valor.Replace("_", ""), ignoreCase: true, out var resultado)
            ? resultado
            : null;

    /// <summary>Como lo escribe el JSON: <c>EnCurso</c> → <c>EN_CURSO</c>.</summary>
    public static string Escribir<T>(T valor) where T : struct, Enum =>
        System.Text.Json.JsonNamingPolicy.SnakeCaseUpper.ConvertName(valor.ToString());
}
