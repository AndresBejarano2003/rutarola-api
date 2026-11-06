namespace RutaRola.Application.Comun;

/// <summary><c>'ACTIVO' | 'INACTIVO'</c> del frontend ↔ columna booleana <c>activo</c>.</summary>
public static class EstadoRegistro
{
    public const string Activo = "ACTIVO";
    public const string Inactivo = "INACTIVO";
    public static readonly string[] Valores = [Activo, Inactivo];

    public static string Texto(bool activo) => activo ? Activo : Inactivo;
    public static bool EsActivo(string? estado) => estado == Activo;
}
