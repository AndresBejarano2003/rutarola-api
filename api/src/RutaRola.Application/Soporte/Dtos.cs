using FluentValidation;
using RutaRola.Application.Comun;
using RutaRola.Domain.Enums;

namespace RutaRola.Application.Soporte;

public record AdjuntoTicketDto(Guid Id, string Nombre, string Tipo, long Tamano);

/// <summary>Archivo nuevo: <c>url</c> es un data URL (PDF o imagen, máximo <see cref="ReglasTicket.TamanoMaximoAdjunto"/>).</summary>
public record AdjuntoNuevoDto(string? Nombre, string? Tipo, string? Url);

public record EventoTicketDto(
    Guid Id, TipoEventoTicket Tipo, EstadoTicket? EstadoAnterior, EstadoTicket? EstadoNuevo, string Texto, bool Interno,
    bool DeLaPlataforma, string Usuario, DateTime Fecha, IReadOnlyList<AdjuntoTicketDto> Adjuntos);

public record ReunionTicketDto(
    Guid Id, DateTime Fecha, int DuracionMinutos, ModalidadReunion Modalidad, string Enlace, string Motivo, EstadoReunion Estado,
    string Notas, string Organizador);

/// <summary>Qué puede hacer la empresa con su ticket en el estado actual.</summary>
public record AccionesClienteDto(bool Comentar, bool Cancelar, bool Validar, bool Confirmar, bool Reabrir);

/// <summary>Fila de los listados (empresa y plataforma). <c>responsableId</c> = <c>""</c> sin asignar.</summary>
public record TicketResumenDto(
    Guid Id, string Numero, Guid EmpresaId, string Empresa, TipoTicket Tipo, string Modulo, PrioridadTicket Prioridad, string Asunto,
    EstadoTicket Estado, string Solicitante, string ResponsableId, string Responsable, DateTime FechaCreacion,
    DateTime FechaUltimaActividad, DateTime FechaObjetivo, bool Vencido, DateTime? ProximaReunion);

/// <summary>
/// Detalle. La empresa NO recibe las notas internas; <c>siguientesEstados</c> solo lo llena la plataforma y
/// <c>acciones</c> solo la empresa.
/// </summary>
public record TicketDto(
    Guid Id, string Numero, Guid EmpresaId, string Empresa, TipoTicket Tipo, string Modulo, PrioridadTicket Prioridad, string Asunto,
    string Descripcion, EstadoTicket Estado, string Solicitante, string CorreoSolicitante, string ResponsableId, string Responsable,
    DateTime FechaCreacion, DateTime FechaUltimaActividad, DateTime FechaObjetivo, bool Vencido, DateTime? FechaPrimeraRespuesta,
    DateTime? FechaResolucion, DateTime? FechaCierre, int? Calificacion, string ComentarioCalificacion,
    IReadOnlyList<AdjuntoTicketDto> Adjuntos, IReadOnlyList<EventoTicketDto> Eventos, IReadOnlyList<ReunionTicketDto> Reuniones,
    IReadOnlyList<EstadoTicket> SiguientesEstados, AccionesClienteDto Acciones);

/// <summary>Cuántos tickets hay en cada estado y cuántos abiertos están vencidos (tablero de la plataforma).</summary>
public record ResumenTicketsDto(IReadOnlyDictionary<string, int> PorEstado, int Abiertos, int Vencidos, int SinAsignar, int ReunionesProximas);

public record ResponsableDto(Guid Id, string Nombre);

public record SolicitudTicket(
    TipoTicket? Tipo, string? Modulo, PrioridadTicket? Prioridad, string? Asunto, string? Descripcion, IReadOnlyList<AdjuntoNuevoDto>? Adjuntos);

/// <summary><c>interno</c> (nota que solo ve la plataforma) se ignora cuando escribe la empresa.</summary>
public record SolicitudComentarioTicket(string? Texto, bool Interno = false, IReadOnlyList<AdjuntoNuevoDto>? Adjuntos = null);

public record SolicitudEstadoTicket(EstadoTicket? Estado, string? Comentario);

public record SolicitudValidacionTicket(bool? Aprobada, string? Comentario);

public record SolicitudConfirmacionTicket(int? Calificacion, string? Comentario);

public record SolicitudMotivoTicket(string? Comentario);

/// <summary><c>responsableId</c> <c>""</c> = sin asignar.</summary>
public record SolicitudAsignacionTicket(string? ResponsableId, PrioridadTicket? Prioridad);

public record SolicitudReunionTicket(DateTime? Fecha, int? DuracionMinutos, ModalidadReunion? Modalidad, string? Enlace, string? Motivo);

/// <summary>REALIZADA (con acta) o CANCELADA (con motivo).</summary>
public record SolicitudEstadoReunion(EstadoReunion? Estado, string? Notas);

/// <summary>
/// <c>estado</c>: un estado, <c>ABIERTOS</c> (sin cerrar, rechazar ni cancelar) o <c>FINALIZADOS</c>.
/// <c>responsableId</c>: un id o <c>SIN_ASIGNAR</c>. <c>empresaId</c> solo aplica a la plataforma.
/// </summary>
public record FiltrosTicket(
    string? Texto = null, string? Estado = null, string? Prioridad = null, string? Tipo = null, Guid? EmpresaId = null,
    string? ResponsableId = null, bool? Vencidos = null);

public class ValidadorSolicitudTicket : AbstractValidator<SolicitudTicket>
{
    public ValidadorSolicitudTicket()
    {
        RuleFor(x => x.Tipo).NotNull().WithMessage("Elige el tipo de solicitud.");
        RuleFor(x => x.Modulo).NotEmpty().WithMessage("Elige el módulo.").MaximumLength(100);
        RuleFor(x => x.Prioridad).NotNull().WithMessage("Elige la prioridad.")
            .NotEqual(PrioridadTicket.Critica).WithMessage("La prioridad crítica la asigna el equipo de soporte.");
        RuleFor(x => x.Asunto).NotEmpty().WithMessage("Escribe el asunto.")
            .Must(a => a is null || a.Trim().Length is >= 5 and <= 120).WithMessage("El asunto debe tener entre 5 y 120 caracteres.");
        RuleFor(x => x.Descripcion).NotEmpty().WithMessage("Describe la solicitud.")
            .Must(d => d is null || d.Trim().Length is >= 20 and <= 2000).WithMessage("La descripción debe tener entre 20 y 2000 caracteres.");
        RuleFor(x => x.Adjuntos).ReglasAdjuntosTicket();
    }
}

public class ValidadorSolicitudComentarioTicket : AbstractValidator<SolicitudComentarioTicket>
{
    public ValidadorSolicitudComentarioTicket()
    {
        RuleFor(x => x.Texto).NotEmpty().WithMessage("Escribe el comentario.").MaximumLength(4000);
        RuleFor(x => x.Adjuntos).ReglasAdjuntosTicket();
    }
}

public class ValidadorSolicitudEstadoTicket : AbstractValidator<SolicitudEstadoTicket>
{
    public ValidadorSolicitudEstadoTicket()
    {
        RuleFor(x => x.Estado).NotNull().WithMessage("Elige el nuevo estado.");
        RuleFor(x => x.Comentario).MaximumLength(4000);
    }
}

public class ValidadorSolicitudValidacionTicket : AbstractValidator<SolicitudValidacionTicket>
{
    public ValidadorSolicitudValidacionTicket()
    {
        RuleFor(x => x.Aprobada).NotNull().WithMessage("Indica si la solución funciona.");
        RuleFor(x => x.Comentario).NotEmpty().When(x => x.Aprobada == false)
            .WithMessage("Cuéntanos qué sigue fallando.").MaximumLength(4000);
    }
}

public class ValidadorSolicitudConfirmacionTicket : AbstractValidator<SolicitudConfirmacionTicket>
{
    public ValidadorSolicitudConfirmacionTicket()
    {
        RuleFor(x => x.Calificacion).NotNull().WithMessage("Califica la atención de 1 a 5.")
            .InclusiveBetween(1, 5).WithMessage("Califica la atención de 1 a 5.");
        RuleFor(x => x.Comentario).MaximumLength(1000);
    }
}

public class ValidadorSolicitudMotivoTicket : AbstractValidator<SolicitudMotivoTicket>
{
    public ValidadorSolicitudMotivoTicket() =>
        RuleFor(x => x.Comentario).NotEmpty().WithMessage("Cuéntanos el motivo.").MaximumLength(4000);
}

public class ValidadorSolicitudAsignacionTicket : AbstractValidator<SolicitudAsignacionTicket>
{
    public ValidadorSolicitudAsignacionTicket()
    {
        RuleFor(x => x.ResponsableId).Must(r => string.IsNullOrEmpty(r) || Guid.TryParse(r, out _)).WithMessage("Responsable no válido.");
        RuleFor(x => x.Prioridad).NotNull().WithMessage("Elige la prioridad.");
    }
}

public class ValidadorSolicitudReunionTicket : AbstractValidator<SolicitudReunionTicket>
{
    public ValidadorSolicitudReunionTicket()
    {
        RuleFor(x => x.Fecha).NotNull().WithMessage("Elige la fecha y la hora.");
        RuleFor(x => x.DuracionMinutos).NotNull().WithMessage("Indica la duración.")
            .InclusiveBetween(15, 480).WithMessage("La duración debe estar entre 15 y 480 minutos.");
        RuleFor(x => x.Modalidad).NotNull().WithMessage("Elige la modalidad.");
        RuleFor(x => x.Enlace).NotEmpty().When(x => x.Modalidad == ModalidadReunion.Virtual)
            .WithMessage("Pega el enlace de la videollamada.").MaximumLength(500);
        RuleFor(x => x.Motivo).NotEmpty().WithMessage("Escribe el objetivo de la reunión.").MaximumLength(300);
    }
}

public class ValidadorSolicitudEstadoReunion : AbstractValidator<SolicitudEstadoReunion>
{
    public ValidadorSolicitudEstadoReunion()
    {
        RuleFor(x => x.Estado).NotNull().WithMessage("Indica si se realizó o se canceló.")
            .NotEqual(EstadoReunion.Programada).WithMessage("Indica si se realizó o se canceló.");
        RuleFor(x => x.Notas).NotEmpty().WithMessage("Escribe el acta o el motivo.").MaximumLength(4000);
    }
}

internal static class ReglasAdjuntos
{
    public static void ReglasAdjuntosTicket<T>(this IRuleBuilder<T, IReadOnlyList<AdjuntoNuevoDto>?> regla) =>
        regla.Must(a => a is null || a.Count <= ReglasTicket.MaximoAdjuntos)
            .WithMessage($"Adjunta máximo {ReglasTicket.MaximoAdjuntos} archivos.")
            .ForEach(a => a.ChildRules(x =>
            {
                x.RuleFor(y => y.Nombre).NotEmpty().WithMessage("Falta el nombre del archivo.").MaximumLength(255);
                x.RuleFor(y => y.Tipo).Must(Adjuntos.TipoPermitido).WithMessage("Adjunta solo PDF o imágenes.");
                x.RuleFor(y => y.Url).Must(Adjuntos.EsDataUrl).WithMessage("El archivo no es válido.");
            }));
}
