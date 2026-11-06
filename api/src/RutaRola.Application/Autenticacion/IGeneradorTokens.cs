namespace RutaRola.Application.Autenticacion;

public record DatosToken(
    Guid IdUsuario,
    string Nombre,
    Guid? IdEmpresa,
    string Rol,
    Guid? IdEmpleado,
    Guid? IdAcudiente,
    Guid IdSesion,
    IReadOnlyCollection<string> Permisos);

public record TokenAcceso(string Token, DateTime Expira);

public interface IGeneradorTokens
{
    TokenAcceso CrearAcceso(DatosToken datos);
    /// <summary>Refresh token aleatorio (256 bits, base64url).</summary>
    string CrearRefresh();
    /// <summary>SHA-256 en hexadecimal: lo único que se guarda del refresh token.</summary>
    string Hash(string refreshToken);
}
