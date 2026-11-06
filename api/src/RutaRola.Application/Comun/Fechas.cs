namespace RutaRola.Application.Comun;

/// <summary>La operación es en Colombia (UTC−5, sin horario de verano): "hoy" es la fecha local.</summary>
public static class Fechas
{
    public static readonly TimeSpan DesfaseColombia = TimeSpan.FromHours(-5);

    public static DateOnly Hoy(TimeProvider reloj) => DateOnly.FromDateTime(reloj.GetUtcNow().ToOffset(DesfaseColombia).DateTime);

    private static readonly string[] Meses = ["ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sept", "oct", "nov", "dic"];

    /// <summary><c>5 nov 2026</c>, como <c>fechaCorta</c> del frontend.</summary>
    public static string Corta(DateOnly fecha) => $"{fecha.Day} {Meses[fecha.Month - 1]} {fecha.Year}";

    /// <summary><c>nov 2026</c>, como <c>nombrePeriodo</c> corto del frontend.</summary>
    public static string MesCorto(DateOnly periodo) => $"{Meses[periodo.Month - 1]} {periodo.Year}";
}
