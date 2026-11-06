using RutaRola.Domain.Comun;
using RutaRola.Domain.Comunicacion;
using RutaRola.Domain.Enums;
using RutaRola.Domain.Personas;
using RutaRola.Domain.Seguridad;

namespace RutaRola.Domain.Operacion;

public class Colegio : EntidadEmpresa
{
    public string Nit { get; set; } = "";
    public string RazonSocial { get; set; } = "";
    public string Direccion { get; set; } = "";
    public string? Telefono { get; set; }
    public string? PersonaContacto { get; set; }
    public decimal? Latitud { get; set; }
    public decimal? Longitud { get; set; }
    public bool Activo { get; set; } = true;
    public List<ColegioCurso> Cursos { get; set; } = [];
}

public class ColegioCurso
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid IdColegio { get; set; }
    public Colegio Colegio { get; set; } = null!;
    public string Grado { get; set; } = "";
    public TimeOnly HoraEntrada { get; set; }
    public TimeOnly HoraSalida { get; set; }
    public bool Activo { get; set; } = true;
}

public class Vehiculo : EntidadEmpresa
{
    public string Placa { get; set; } = "";
    public string NumeroInterno { get; set; } = "";
    public TipoVehiculo Tipo { get; set; }
    public string Marca { get; set; } = "";
    /// <summary>Línea del vehículo (columna <c>modelo</c> del diagrama inicial).</summary>
    public string? Linea { get; set; }
    /// <summary>Año modelo (el frontend lo llama <c>modelo</c>).</summary>
    public int Anio { get; set; }
    public string? Color { get; set; }
    public int Capacidad { get; set; }
    public string? Observaciones { get; set; }
    public bool Activo { get; set; } = true;
    public List<DocumentoVehiculo> Documentos { get; set; } = [];
}

/// <summary>SOAT, técnico-mecánica, tarjeta de operación y pólizas: uno vigente por tipo.</summary>
public class DocumentoVehiculo
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid IdVehiculo { get; set; }
    public Vehiculo Vehiculo { get; set; } = null!;
    public TipoDocumentoVehiculo TipoDocumento { get; set; }
    public string? NumeroDocumento { get; set; }
    public DateOnly FechaVencimiento { get; set; }
    public Guid? IdArchivo { get; set; }
    public Archivo? Archivo { get; set; }
}

public class Ruta : EntidadEmpresa
{
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public Guid IdColegio { get; set; }
    public Colegio Colegio { get; set; } = null!;
    public string Jornada { get; set; } = "";
    public string? Sector { get; set; }
    public string? Observaciones { get; set; }
    public bool Activo { get; set; } = true;
    public List<RutaEstudiante> Estudiantes { get; set; } = [];
}

public class RutaEstudiante
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid IdRuta { get; set; }
    public Ruta Ruta { get; set; } = null!;
    public Guid IdEstudiante { get; set; }
    public Estudiante Estudiante { get; set; } = null!;
    public int OrdenRecogida { get; set; }
    public TimeOnly? HoraRecogidaEstimada { get; set; }
    public TimeOnly? HoraLlegadaEstimada { get; set; }
    public bool Activo { get; set; } = true;
}

/// <summary>Quién cubre una ruta y cuándo: vehículo + conductor + monitora entre dos fechas.</summary>
public class AsignacionRuta : EntidadEmpresa
{
    public Guid IdRuta { get; set; }
    public Ruta Ruta { get; set; } = null!;
    public Guid IdVehiculo { get; set; }
    public Vehiculo Vehiculo { get; set; } = null!;
    public Guid IdConductor { get; set; }
    public Empleado Conductor { get; set; } = null!;
    public Guid? IdMonitora { get; set; }
    public Empleado? Monitora { get; set; }
    public DateOnly FechaInicio { get; set; }
    /// <summary>Null = indefinida.</summary>
    public DateOnly? FechaFin { get; set; }
    public string? Observaciones { get; set; }
    public bool Activo { get; set; } = true;
}

/// <summary>
/// Recorrido de un trayecto en una fecha (lo que el frontend llama <c>Recorrido</c>). Guarda un
/// snapshot de la ruta, el colegio, el vehículo y el personal al iniciar.
/// </summary>
public class ViajeRuta : Entidad
{
    public Guid IdRuta { get; set; }
    public Ruta Ruta { get; set; } = null!;
    public Guid IdAsignacion { get; set; }
    public AsignacionRuta Asignacion { get; set; } = null!;
    public DateOnly FechaViaje { get; set; }
    public Trayecto Trayecto { get; set; }
    public Guid IdConductor { get; set; }
    public Empleado Conductor { get; set; } = null!;
    public Guid? IdMonitora { get; set; }
    public Empleado? Monitora { get; set; }
    public Guid IdVehiculo { get; set; }
    public Vehiculo Vehiculo { get; set; } = null!;
    public DateTime? HoraInicio { get; set; }
    public DateTime? HoraFin { get; set; }
    public EstadoViaje Estado { get; set; } = EstadoViaje.Programado;
    public Guid? IdIniciadoPor { get; set; }
    public Empleado? IniciadoPor { get; set; }

    // Snapshot al iniciar
    public string NombreRuta { get; set; } = "";
    public string Jornada { get; set; } = "";
    public string NombreColegio { get; set; } = "";
    public string? DireccionColegio { get; set; }
    public string DescripcionVehiculo { get; set; } = "";
    public string NombreConductor { get; set; } = "";
    public string? NombreMonitora { get; set; }

    /// <summary>Último cambio del orden de visita.</summary>
    public Guid? IdOrdenActualizadoPor { get; set; }
    public string? NombreOrdenActualizadoPor { get; set; }
    public DateTime? FechaOrdenActualizado { get; set; }

    public string? Observaciones { get; set; }
    /// <summary>Concurrencia optimista (xmin): conductor y monitora editan el mismo recorrido.</summary>
    public uint Version { get; set; }

    public List<ParadaViaje> Paradas { get; set; } = [];
    public List<Evento> Eventos { get; set; } = [];
}

public class ParadaViaje
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid IdViaje { get; set; }
    public ViajeRuta Viaje { get; set; } = null!;
    public Guid IdEstudiante { get; set; }
    public Estudiante Estudiante { get; set; } = null!;
    public int OrdenRecogida { get; set; }
    public EstadoParada Estado { get; set; } = EstadoParada.Pendiente;
    /// <summary>Cuándo subió al vehículo.</summary>
    public DateTime? HoraRecogida { get; set; }
    /// <summary>Cuándo se entregó (colegio en recogida, casa en entrega).</summary>
    public DateTime? HoraEntrega { get; set; }
    public string? FotoUrl { get; set; }
    public string? Notas { get; set; }

    // Snapshot al iniciar
    public string NombreEstudiante { get; set; } = "";
    public string Grado { get; set; } = "";
    public string Direccion { get; set; } = "";
    public string? NombreAcudiente { get; set; }
    public string? TelefonoAcudiente { get; set; }
}

public class Evento
{
    public long Id { get; set; }
    public Guid IdViaje { get; set; }
    public ViajeRuta Viaje { get; set; } = null!;
    public TipoEvento TipoEvento { get; set; }
    public string? Descripcion { get; set; }
    public Guid? IdUsuario { get; set; }
    public Usuario? Usuario { get; set; }
    public DateTime FechaEvento { get; set; }
}

/// <summary>Posición reportada por el conductor. La tabla más grande: id bigint.</summary>
public class GpsVehiculo
{
    public long IdGps { get; set; }
    public Guid IdVehiculo { get; set; }
    public Vehiculo Vehiculo { get; set; } = null!;
    public Guid IdViaje { get; set; }
    public ViajeRuta Viaje { get; set; } = null!;
    public decimal Latitud { get; set; }
    public decimal Longitud { get; set; }
    /// <summary>km/h.</summary>
    public decimal? Velocidad { get; set; }
    /// <summary>Grados 0–360 (antes <c>direccion</c>).</summary>
    public decimal? Rumbo { get; set; }
    /// <summary>Momento de la lectura en el dispositivo (UTC).</summary>
    public DateTime FechaRegistro { get; set; }
}
