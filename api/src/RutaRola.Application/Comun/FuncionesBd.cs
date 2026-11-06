using System.Globalization;
using System.Text;

namespace RutaRola.Application.Comun;

/// <summary>
/// Funciones que se traducen a SQL (mapeadas en <c>RutaRolaDbContext</c>). Solo valen dentro de consultas LINQ.
/// </summary>
public static class FuncionesBd
{
    /// <summary>
    /// <c>f_sin_tildes(texto)</c>: minúsculas y sin tildes (extensión unaccent). Las búsquedas por texto
    /// comparan <c>SinTildes(columna).Contains(Busqueda.Normalizar(texto))</c>, igual que <c>normalizar()</c> del frontend.
    /// </summary>
    public static string SinTildes(string? texto) => throw new NotSupportedException("FuncionesBd.SinTildes solo se usa en consultas LINQ.");
}

public static class Busqueda
{
    /// <summary>Minúsculas y sin tildes, en C# (el lado del parámetro de una búsqueda).</summary>
    public static string Normalizar(string texto)
    {
        var descompuesto = texto.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(descompuesto.Length);
        foreach (var c in descompuesto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
