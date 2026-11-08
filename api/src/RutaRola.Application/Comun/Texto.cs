namespace RutaRola.Application.Comun;

public static class Texto
{
    /// <summary>Recorta espacios; cadenas vacías → null (columnas opcionales).</summary>
    public static string? Opcional(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    /// <summary>Usuario de ingreso o correo normalizado: sin espacios a los lados y en minúsculas.</summary>
    public static string Usuario(string? valor) => (valor ?? "").Trim().ToLowerInvariant();

    /// <summary>Corta el texto a <paramref name="maximo"/> caracteres (con "…").</summary>
    public static string Recortar(string valor, int maximo) => valor.Length <= maximo ? valor : string.Concat(valor.AsSpan(0, maximo - 1), "…");
}
