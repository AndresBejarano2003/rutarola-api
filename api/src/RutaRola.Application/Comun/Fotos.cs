using FluentValidation;

namespace RutaRola.Application.Comun;

/// <summary>
/// Foto de perfil (personas y usuarios): data URL JPG, PNG o WebP. El frontend la recorta y reduce a 256 px
/// (unos 20–40 KB), así que cabe en la fila y se devuelve también en los listados para el avatar de las tablas.
/// <c>""</c> o null = sin foto (se muestran las iniciales).
/// </summary>
public static class Fotos
{
    /// <summary>Unos 300 KB de imagen en base64.</summary>
    public const int MaximoCaracteres = 400_000;
    public const string Mensaje = "La foto debe ser una imagen JPG, PNG o WebP de máximo 300 KB.";

    private static readonly string[] Prefijos = ["data:image/jpeg;base64,", "data:image/png;base64,", "data:image/webp;base64,"];

    public static bool EsValida(string? foto) =>
        string.IsNullOrEmpty(foto)
        || (foto.Length <= MaximoCaracteres
            && Prefijos.Any(p => foto.StartsWith(p, StringComparison.Ordinal))
            && EsBase64(foto[(foto.IndexOf(',') + 1)..]));

    /// <summary>Para guardar: null si viene vacía.</summary>
    public static string? Normalizar(string? foto) => string.IsNullOrWhiteSpace(foto) ? null : foto.Trim();

    public static IRuleBuilderOptions<T, string?> FotoValida<T>(this IRuleBuilder<T, string?> regla) =>
        regla.Must(EsValida).WithMessage(Mensaje);

    private static bool EsBase64(string texto)
    {
        var bytes = new byte[texto.Length];
        return Convert.TryFromBase64String(texto, bytes, out _);
    }
}
