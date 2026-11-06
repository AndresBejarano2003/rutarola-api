namespace RutaRola.Application.Comun;

/// <summary>
/// Paginación 1-based (<c>?pagina=1&amp;tamano=10</c>), igual que el frontend. Acepta números grandes
/// (el frontend pide <c>tamano=Number.MAX_SAFE_INTEGER</c> para "todo") y los acota a
/// <see cref="TamanoMaximo"/>; la respuesta trae el tamaño aplicado.
/// </summary>
public record Paginacion(long Pagina = 1, long Tamano = 10)
{
    public const int TamanoMaximo = 1000;
    private const int PaginaMaxima = 1_000_000;

    public int PaginaValida => (int)Math.Clamp(Pagina, 1, PaginaMaxima);
    public int TamanoValido => (int)Math.Clamp(Tamano, 1, TamanoMaximo);
    public int Omitir => (int)Math.Min(int.MaxValue, (long)(PaginaValida - 1) * TamanoValido);
}

/// <summary>Respuesta de los listados: <c>{ items, total, pagina, tamano }</c>.</summary>
public record ListaPaginada<T>(IReadOnlyList<T> Items, int Total, int Pagina, int Tamano);
