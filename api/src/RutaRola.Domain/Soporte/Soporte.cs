using RutaRola.Domain.Comun;
using RutaRola.Domain.Enums;
using RutaRola.Domain.Seguridad;

namespace RutaRola.Domain.Soporte;

/// <summary>
/// Solicitud de soporte de una empresa a la plataforma (tabla <c>tickets_soporte</c>). La crea un
/// administrador de la empresa; la gestiona el SuperAdmin. Número global <c>SOP-0001</c>.
/// Los nombres y el correo del solicitante se guardan como estaban al crear (snapshot).
/// </summary>
public class TicketSoporte : EntidadEmpresa
{
    public string Numero { get; set; } = "";
    public TipoTicket Tipo { get; set; }
    /// <summary>Módulo del menú en que ocurre (texto del frontend: "Personas › Estudiantes"…).</summary>
    public string Modulo { get; set; } = "";
    public PrioridadTicket Prioridad { get; set; } = PrioridadTicket.Media;
    public string Asunto { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public EstadoTicket Estado { get; set; } = EstadoTicket.Nuevo;

    public Guid IdSolicitante { get; set; }
    public Usuario Solicitante { get; set; } = null!;
    public string NombreSolicitante { get; set; } = "";
    public string? CorreoSolicitante { get; set; }

    /// <summary>Usuario de la plataforma (SuperAdmin) a cargo.</summary>
    public Guid? IdResponsable { get; set; }
    public Usuario? Responsable { get; set; }
    /// <summary>Nombre del responsable al asignarlo (la empresa no puede leer usuarios de la plataforma).</summary>
    public string? NombreResponsable { get; set; }

    /// <summary>Hasta cuándo debería quedar resuelto según la prioridad (SLA).</summary>
    public DateTime FechaObjetivo { get; set; }
    public DateTime? FechaPrimeraRespuesta { get; set; }
    public DateTime? FechaResolucion { get; set; }
    public DateTime? FechaCierre { get; set; }
    public DateTime FechaUltimaActividad { get; set; }

    /// <summary>1–5, la pone el cliente al confirmar la solución.</summary>
    public int? Calificacion { get; set; }
    public string? ComentarioCalificacion { get; set; }

    public List<EventoTicket> Eventos { get; set; } = [];
    public List<ReunionTicket> Reuniones { get; set; } = [];
    public List<AdjuntoTicket> Adjuntos { get; set; } = [];
}

/// <summary>Historial del ticket: creación, cambios de estado, comentarios, notas internas, asignaciones…</summary>
public class EventoTicket
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid IdTicket { get; set; }
    public TicketSoporte Ticket { get; set; } = null!;
    public TipoEventoTicket Tipo { get; set; }
    public EstadoTicket? EstadoAnterior { get; set; }
    public EstadoTicket? EstadoNuevo { get; set; }
    public string? Texto { get; set; }
    /// <summary>Solo lo ve la plataforma (notas internas).</summary>
    public bool Interno { get; set; }
    /// <summary>Lo hizo la plataforma (true) o la empresa (false).</summary>
    public bool DeLaPlataforma { get; set; }
    public Guid? IdUsuario { get; set; }
    public string NombreUsuario { get; set; } = "";
    public DateTime Fecha { get; set; }
}

/// <summary>Reunión agendada con el cliente para el ticket (diagnóstico, capacitación, validación…).</summary>
public class ReunionTicket
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid IdTicket { get; set; }
    public TicketSoporte Ticket { get; set; } = null!;
    /// <summary>Inicio en UTC.</summary>
    public DateTime Fecha { get; set; }
    public int DuracionMinutos { get; set; } = 30;
    public ModalidadReunion Modalidad { get; set; }
    /// <summary>Enlace de la videollamada, dirección o teléfono según la modalidad.</summary>
    public string? Enlace { get; set; }
    public string Motivo { get; set; } = "";
    public EstadoReunion Estado { get; set; } = EstadoReunion.Programada;
    /// <summary>Acta o motivo de cancelación.</summary>
    public string? Notas { get; set; }
    public string NombreOrganizador { get; set; } = "";
    public DateTime FechaCreacion { get; set; }
}

/// <summary>Archivo (PDF o imagen ≤ 1 MB) adjunto al ticket o a uno de sus comentarios.</summary>
public class AdjuntoTicket
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid IdTicket { get; set; }
    public TicketSoporte Ticket { get; set; } = null!;
    public Guid? IdEvento { get; set; }
    public EventoTicket? Evento { get; set; }
    public string Nombre { get; set; } = "";
    public string Tipo { get; set; } = "";
    public long Tamano { get; set; }
    public byte[] Contenido { get; set; } = [];
    public DateTime FechaCreacion { get; set; }
}
