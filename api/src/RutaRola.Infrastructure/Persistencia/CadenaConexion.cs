using Npgsql;

namespace RutaRola.Infrastructure.Persistencia;

/// <summary>
/// Acepta la cadena de conexión como la entregan los proveedores (Render, Neon, Railway, Heroku):
/// <c>postgresql://usuario:clave@host:puerto/base?sslmode=require</c>, además del formato de Npgsql
/// (<c>Host=…;Database=…</c>), que se deja igual.
/// </summary>
public static class CadenaConexion
{
    public static string Normalizar(string cadena)
    {
        if (!cadena.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !cadena.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return cadena;
        }
        var uri = new Uri(cadena);
        var credenciales = uri.UserInfo.Split(':', 2);
        var b = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(credenciales[0]),
            Password = credenciales.Length > 1 ? Uri.UnescapeDataString(credenciales[1]) : null,
        };
        foreach (var parte in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var par = parte.Split('=', 2);
            if (par.Length == 2 && par[0].Equals("sslmode", StringComparison.OrdinalIgnoreCase)
                && Enum.TryParse<SslMode>(Uri.UnescapeDataString(par[1]).Replace("-", ""), true, out var ssl))
            {
                b.SslMode = ssl;
            }
        }
        return b.ConnectionString;
    }
}
