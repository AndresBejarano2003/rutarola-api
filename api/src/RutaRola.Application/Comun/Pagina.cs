namespace RutaRola.Application.Comun;

/// <summary>Paginación 1-based (<c>?pagina=1&amp;tamano=10</c>), igual que el frontend.</summary>
public record Paginacion(int Pagina = 1, int Tamano = 10)
{
    public const int TamanoMaximo = 100;

    public int PaginaValida => Math.Max(1, Pagina);
    public int TamanoValido => Math.Clamp(Tamano, 1, TamanoMaximo);
    public int Omitir => (PaginaValida - 1) * TamanoValido;
}

/// <summary>Respuesta de los listados: <c>{ items, total, pagina, tamano }</c>.</summary>
public record ListaPaginada<T>(IReadOnlyList<T> Items, int Total, int Pagina, int Tamano);
