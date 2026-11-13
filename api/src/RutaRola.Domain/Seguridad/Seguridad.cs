using RutaRola.Domain.Comun;
using RutaRola.Domain.Enums;

namespace RutaRola.Domain.Seguridad;

public class Empresa : Entidad
{
    public string Nombre { get; set; } = "";
    public string Nit { get; set; } = "";
    public string? Direccion { get; set; }
    public string? Ciudad { get; set; }
    public string? Telefono { get; set; }
    public string? Email { get; set; }
    public string? LogoUrl { get; set; }
    public bool Activo { get; set; } = true;
    /// <summary>Tema de la aplicación para los usuarios de la empresa: OSCURO (por defecto) o CLARO.</summary>
    public TemaApariencia Tema { get; set; } = TemaApariencia.Oscuro;
}

/// <summary>
/// Encabezado y pie de los PDF de la empresa (facturas, recibos, cotizaciones). Una fila por empresa; sin fila se
/// usan los datos de contacto de la empresa.
/// </summary>
public class FormatoDocumentos : EntidadEmpresa
{
    /// <summary>Data URL PNG/JPG/WebP/SVG; null = sin logo.</summary>
    public string? Logo { get; set; }
    public string? NombreComercial { get; set; }
    public string? Eslogan { get; set; }
    public string? Nit { get; set; }
    public string? Direccion { get; set; }
    public string? Ciudad { get; set; }
    public string? Telefono { get; set; }
    public string? Correo { get; set; }
    public string? SitioWeb { get; set; }
    /// <summary>#RRGGBB de títulos y bordes.</summary>
    public string ColorPrincipal { get; set; } = "#1F2937";
    /// <summary>Texto al pie de todos los documentos (resolución, cuentas bancarias…).</summary>
    public string? PiePagina { get; set; }
    public string? NotaFactura { get; set; }
    public string? NotaRecibo { get; set; }
    public string? NotaCotizacion { get; set; }
}

/// <summary>
/// Credenciales y rol. Los datos personales de conductores, monitoras y acudientes viven en su
/// registro de Personas (que apunta aquí con id_usuario); <see cref="Nombre"/> y <see cref="Telefono"/>
/// sirven para administradores, que no tienen ficha de persona.
/// </summary>
public class Usuario : Entidad
{
    /// <summary>Null solo para SuperAdmin (plataforma).</summary>
    public Guid? IdEmpresa { get; set; }
    public Empresa? Empresa { get; set; }
    public Guid IdRol { get; set; }
    public Rol Rol { get; set; } = null!;
    public string Nombre { get; set; } = "";
    /// <summary>Usuario de ingreso: único en todo el sistema, en minúsculas, 4–40 caracteres sin espacios.</summary>
    public string NombreUsuario { get; set; } = "";
    public string? Email { get; set; }
    public string? Telefono { get; set; }
    /// <summary>Foto de perfil de administradores y SuperAdmin (data URL). La de conductores, monitoras y acudientes está en su registro.</summary>
    public string? Foto { get; set; }
    public string PasswordHash { get; set; } = "";
    public bool Activo { get; set; } = true;
    public DateTime? UltimoLogin { get; set; }
}

public class Rol : Entidad
{
    public string Nombre { get; set; } = "";
    public string? Descripcion { get; set; }
    public List<RolPermiso> Permisos { get; set; } = [];
}

public class Permiso : Entidad
{
    /// <summary><c>modulo.accion</c>, p. ej. <c>estudiantes.gestionar</c>.</summary>
    public string Nombre { get; set; } = "";
    public string? Descripcion { get; set; }
}

public class RolPermiso
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid IdRol { get; set; }
    public Rol Rol { get; set; } = null!;
    public Guid IdPermiso { get; set; }
    public Permiso Permiso { get; set; } = null!;
}

/// <summary>Refresh token rotativo: se guarda SOLO su hash (SHA-256), nunca el token.</summary>
public class SesionUsuario
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid IdUsuario { get; set; }
    public Usuario Usuario { get; set; } = null!;
    public string TokenHash { get; set; } = "";
    public string? DireccionIp { get; set; }
    public string? Dispositivo { get; set; }
    public DateTime FechaCreacion { get; set; }
    public DateTime FechaExpiracion { get; set; }
    public bool Revocado { get; set; }
    public DateTime? FechaRevocacion { get; set; }
    /// <summary>Sesión que la reemplazó al rotar (para detectar reuso de un token ya rotado).</summary>
    public Guid? IdReemplazadaPor { get; set; }
    /// <summary>Concurrencia optimista (xmin): dos renovaciones simultáneas del mismo token, solo una gana.</summary>
    public uint Version { get; set; }
}

public class Auditoria
{
    public long Id { get; set; }
    public Guid? IdEmpresa { get; set; }
    public Guid? IdUsuario { get; set; }
    public AccionAuditoria Accion { get; set; }
    public string TablaAfectada { get; set; } = "";
    public Guid IdRegistro { get; set; }
    /// <summary>JSON (jsonb) del registro antes del cambio; sin contraseñas ni archivos.</summary>
    public string? ValorAnterior { get; set; }
    public string? ValorNuevo { get; set; }
    public DateTime FechaAccion { get; set; }
    public string? DireccionIp { get; set; }
}

public class ConfiguracionSistema : EntidadEmpresa
{
    public string Clave { get; set; } = "";
    public string Valor { get; set; } = "";
}
