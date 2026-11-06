namespace RutaRola.Application.Comun;

/// <summary>Catálogos fijos del frontend (<c>shared/catalogos/catalogos.ts</c>).</summary>
public static class Catalogos
{
    public static readonly string[] TiposDocumento = ["RC", "TI", "CC", "CE", "PPT", "PA"];

    /// <summary>En orden: Jardín, Transición, 1°…11°.</summary>
    public static readonly string[] Grados = ["Jardín", "Transición", .. Enumerable.Range(1, 11).Select(n => $"{n}°")];

    public static readonly string[] Jornadas = ["Mañana", "Tarde", "Única"];

    /// <summary>Orden en que se muestran las rutas del día.</summary>
    public static readonly string[] OrdenJornadas = ["Mañana", "Única", "Tarde", "Noche"];

    private static readonly string[] NombresGrados =
        ["Primero", "Segundo", "Tercero", "Cuarto", "Quinto", "Sexto", "Séptimo", "Octavo", "Noveno", "Décimo", "Once"];

    /// <summary><c>7°</c> → <c>7° · Séptimo</c> (nombre del catálogo <c>GRADOS</c>).</summary>
    public static string NombreGrado(string grado)
    {
        var orden = OrdenGrado(grado);
        return orden >= 2 ? $"{grado} · {NombresGrados[orden - 2]}" : grado;
    }

    public static int OrdenJornada(string jornada) => Array.IndexOf(OrdenJornadas, jornada) is var i and >= 0 ? i : OrdenJornadas.Length;

    public static readonly string[] CategoriasLicencia = ["B1", "B2", "B3", "C1", "C2", "C3"];

    /// <summary>Días antes del vencimiento en que un documento o licencia está "por vencer".</summary>
    public const int DiasAlertaVencimiento = 30;

    public static int OrdenGrado(string grado) => Array.IndexOf(Grados, grado);

    /// <summary><c>VIGENTE | POR_VENCER | VENCIDA</c> (igual que <c>estadoVencimiento</c> del frontend).</summary>
    /// <param name="diasAlerta">Parámetro <c>alertas.dias_vencimiento</c> de la empresa (<c>ParametrosEmpresa</c>).</param>
    public static string? EstadoVencimiento(DateOnly? vencimiento, DateOnly hoy, int diasAlerta = DiasAlertaVencimiento) =>
        vencimiento switch
        {
            null => null,
            { } f when f < hoy => "VENCIDA",
            { } f when f <= hoy.AddDays(diasAlerta) => "POR_VENCER",
            _ => "VIGENTE",
        };
}
