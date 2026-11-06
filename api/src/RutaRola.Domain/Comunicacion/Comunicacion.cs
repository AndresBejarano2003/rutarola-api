using RutaRola.Domain.Comun;
using RutaRola.Domain.Enums;
using RutaRola.Domain.Personas;
using RutaRola.Domain.Seguridad;

namespace RutaRola.Domain.Comunicacion;

/// <summary>
/// Notificación del usuario. La crea el servidor; <see cref="Clave"/> identifica el hecho y es única
/// por usuario: borrar es lógico (<see cref="Eliminada"/>) para no volver a notificar lo mismo.
/// </summary>
public class Notificacion
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid IdUsuario { get; set; }
    public Usuario Usuario { get; set; } = null!;
    public string Clave { get; set; } = "";
    public CategoriaNotificacion Categoria { get; set; }
    public NivelNotificacion Nivel { get; set; }
    public string Titulo { get; set; } = "";
    public string Mensaje { get; set; } = "";
    public DateTime FechaCreacion { get; set; }
    public bool Leida { get; set; }
    public bool Archivada { get; set; }
    public bool Eliminada { get; set; }
    public string? Enlace { get; set; }
    public string? TextoEnlace { get; set; }
}

/// <summary>Registro de mensajes de WhatsApp (la integración con WhatsApp Business va después).</summary>
public class MensajeWhatsapp
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid IdAcudiente { get; set; }
    public Acudiente Acudiente { get; set; } = null!;
    public Guid? IdEstudiante { get; set; }
    public Estudiante? Estudiante { get; set; }
    public string Telefono { get; set; } = "";
    public string Mensaje { get; set; } = "";
    public EstadoMensaje Estado { get; set; } = EstadoMensaje.Pendiente;
    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaEnvio { get; set; }
    public string? Respuesta { get; set; }
    /// <summary>Id del mensaje en el proveedor (WhatsApp Business).</summary>
    public string? IdExterno { get; set; }
}

/// <summary>
/// Archivo subido (contrato, documento de vehículo, adjunto de plantilla). Por ahora el contenido se
/// guarda en la BD (≤ 2 MB); con almacenamiento externo se usa <see cref="UrlArchivo"/>.
/// </summary>
public class Archivo : EntidadEmpresa
{
    public string Nombre { get; set; } = "";
    /// <summary>Tipo MIME.</summary>
    public string TipoArchivo { get; set; } = "";
    public long Tamano { get; set; }
    public string? UrlArchivo { get; set; }
    public byte[]? Contenido { get; set; }
    public Guid? SubidoPor { get; set; }
    public Usuario? Usuario { get; set; }
}
