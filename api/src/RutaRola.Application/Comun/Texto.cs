namespace RutaRola.Application.Comun;

public static class Texto
{
    /// <summary>Recorta espacios; cadenas vacías → null (columnas opcionales).</summary>
    public static string? Opcional(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    /// <summary>Usuario de ingreso o correo normalizado: sin espacios a los lados y en minúsculas.</summary>
    public static string Usuario(string? valor) => (valor ?? "").Trim().ToLowerInvariant();
}
