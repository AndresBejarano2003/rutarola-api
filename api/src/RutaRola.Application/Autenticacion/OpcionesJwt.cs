namespace RutaRola.Application.Autenticacion;

/// <summary>Sección <c>Jwt</c>. La clave viene de <c>Jwt__Clave</c> (variable de entorno o user-secrets), nunca del repo.</summary>
public class OpcionesJwt
{
    public const string Seccion = "Jwt";

    /// <summary>Clave HMAC-SHA256: mínimo 32 caracteres.</summary>
    public string Clave { get; set; } = "";
    public string Emisor { get; set; } = "rutarola-api";
    public string Audiencia { get; set; } = "rutarola";
    public int MinutosAcceso { get; set; } = 15;
    public int DiasRefresh { get; set; } = 30;
    /// <summary>
    /// Segundos en los que reusar un refresh recién rotado NO se trata como robo (dos pestañas que
    /// renuevan a la vez): se responde 401 sin revocar las demás sesiones.
    /// </summary>
    public int SegundosGraciaRotacion { get; set; } = 30;
}
