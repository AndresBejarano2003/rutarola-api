namespace RutaRola.Application.Comun;

/// <summary>
/// Usuario de la petición actual, leído de los claims del JWT. Es la ÚNICA fuente de la empresa:
/// el id_empresa nunca se toma del body ni de la URL.
/// </summary>
public interface IContextoUsuario
{
    bool Autenticado { get; }
    Guid? IdUsuario { get; }
    /// <summary>Null para anónimos y para SuperAdmin.</summary>
    Guid? IdEmpresa { get; }
    string? Rol { get; }
    Guid? IdEmpleado { get; }
    Guid? IdAcudiente { get; }
    Guid? IdSesion { get; }
    string? DireccionIp { get; }
}
