namespace RutaRola.Application.Comun;

/// <summary>La operación es en Colombia (UTC−5, sin horario de verano): "hoy" es la fecha local.</summary>
public static class Fechas
{
    public static readonly TimeSpan DesfaseColombia = TimeSpan.FromHours(-5);

    public static DateOnly Hoy(TimeProvider reloj) => DateOnly.FromDateTime(reloj.GetUtcNow().ToOffset(DesfaseColombia).DateTime);
}
