using RutaRola.Domain.Comun;
using RutaRola.Domain.Comunicacion;
using RutaRola.Domain.Enums;
using RutaRola.Domain.Operacion;
using RutaRola.Domain.Seguridad;

namespace RutaRola.Domain.Personas;

/// <summary>Conductor o monitora (reemplaza a <c>conductores</c> del modelo inicial).</summary>
public class Empleado : Persona
{
    public Guid? IdUsuario { get; set; }
    public Usuario? Usuario { get; set; }
    public TipoEmpleado TipoEmpleado { get; set; }
    /// <summary>Contacto de la persona (se muestra en el portal y en los recorridos); no se duplica en usuarios.</summary>
    public string Telefono { get; set; } = "";
    public string? Correo { get; set; }
    public string? Direccion { get; set; }
    public DateOnly FechaIngreso { get; set; }
    public decimal Sueldo { get; set; }
    public TipoContrato TipoContrato { get; set; }
    public Guid? IdArchivoContrato { get; set; }
    public Archivo? ArchivoContrato { get; set; }
    /// <summary>Obligatoria para conductores; vacía para monitoras.</summary>
    public string? NumeroLicencia { get; set; }
    public string? CategoriaLicencia { get; set; }
    public DateOnly? FechaVencimientoLicencia { get; set; }
    public bool Activo { get; set; } = true;
}

public class Acudiente : Persona
{
    /// <summary>Acceso al portal del acudiente.</summary>
    public Guid? IdUsuario { get; set; }
    public Usuario? Usuario { get; set; }
    public string Telefono { get; set; } = "";
    public string? TelefonoAlterno { get; set; }
    public string? Correo { get; set; }
    public string? Direccion { get; set; }
    public string? Parentesco { get; set; }
    public bool Activo { get; set; } = true;
    public List<EstudianteAcudiente> Estudiantes { get; set; } = [];
}

public class Estudiante : Persona
{
    public Guid IdColegio { get; set; }
    public Colegio Colegio { get; set; } = null!;
    public DateOnly FechaNacimiento { get; set; }
    public string Direccion { get; set; } = "";
    public decimal? Latitud { get; set; }
    public decimal? Longitud { get; set; }
    /// <summary>Jardín, Transición, 1°…11° (catálogo fijo del frontend).</summary>
    public string Grado { get; set; } = "";
    /// <summary>Mañana, Tarde, Única.</summary>
    public string Jornada { get; set; } = "";
    public string? Observaciones { get; set; }
    public bool Activo { get; set; } = true;
    public List<EstudianteAcudiente> Acudientes { get; set; } = [];
}

/// <summary>Un estudiante puede tener varios acudientes; uno es el principal (el <c>acudienteId</c> del frontend).</summary>
public class EstudianteAcudiente
{
    public Guid IdEstudiante { get; set; }
    public Estudiante Estudiante { get; set; } = null!;
    public Guid IdAcudiente { get; set; }
    public Acudiente Acudiente { get; set; } = null!;
    public bool EsPrincipal { get; set; }
}
