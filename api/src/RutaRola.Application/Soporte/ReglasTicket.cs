using RutaRola.Domain.Enums;

namespace RutaRola.Application.Soporte;

/// <summary>
/// Flujo de un ticket (igual que <c>tickets.ts</c> del frontend):
/// <code>
/// NUEVO → EN_REVISION → EN_PROCESO → EN_QA → EN_PRUEBAS → VALIDACION_CLIENTE → RESUELTO → CERRADO
///            ↕ ESPERANDO_CLIENTE        ↖ devuelto ↙          ↖ el cliente rechaza
/// NUEVO / EN_REVISION → RECHAZADO (no procede); el cliente cancela mientras no esté resuelto.
/// </code>
/// Errores y mejoras pasan SIEMPRE por QA, pruebas y validación del cliente; consultas y capacitaciones
/// pueden resolverse desde la revisión o el proceso. Las devoluciones y cierres piden comentario.
/// </summary>
public static class ReglasTicket
{
    public const int MaximoAdjuntos = 3;
    public const int TamanoMaximoAdjunto = 1024 * 1024;
    /// <summary>Un ticket RESUELTO sin respuesta del cliente se cierra solo después de estos días.</summary>
    public const int DiasCierreAutomatico = 7;

    /// <summary>Tiempo objetivo de solución (SLA) desde la creación.</summary>
    public static TimeSpan Objetivo(PrioridadTicket prioridad) => prioridad switch
    {
        PrioridadTicket.Critica => TimeSpan.FromHours(8),
        PrioridadTicket.Alta => TimeSpan.FromHours(24),
        PrioridadTicket.Media => TimeSpan.FromHours(72),
        _ => TimeSpan.FromDays(7),
    };

    public static readonly IReadOnlySet<EstadoTicket> Finalizados =
        new HashSet<EstadoTicket> { EstadoTicket.Cerrado, EstadoTicket.Rechazado, EstadoTicket.Cancelado };

    /// <summary>Sin SLA corriendo: ya está resuelto o finalizado.</summary>
    public static readonly IReadOnlySet<EstadoTicket> SinVencimiento =
        new HashSet<EstadoTicket> { EstadoTicket.Resuelto, EstadoTicket.Cerrado, EstadoTicket.Rechazado, EstadoTicket.Cancelado };

    public static bool Vencido(EstadoTicket estado, DateTime objetivo, DateTime ahora) => !SinVencimiento.Contains(estado) && ahora > objetivo;

    /// <summary>Errores y mejoras exigen QA, pruebas y validación del cliente antes de resolverse.</summary>
    public static bool RequiereCalidad(TipoTicket tipo) => tipo is TipoTicket.Error or TipoTicket.Mejora;

    private static readonly Dictionary<EstadoTicket, EstadoTicket[]> Transiciones = new()
    {
        [EstadoTicket.Nuevo] = [EstadoTicket.EnRevision, EstadoTicket.Rechazado],
        [EstadoTicket.EnRevision] = [EstadoTicket.EnProceso, EstadoTicket.EsperandoCliente, EstadoTicket.Resuelto, EstadoTicket.Rechazado],
        [EstadoTicket.EsperandoCliente] = [EstadoTicket.EnRevision, EstadoTicket.EnProceso],
        [EstadoTicket.EnProceso] = [EstadoTicket.EnQa, EstadoTicket.EsperandoCliente, EstadoTicket.EnRevision, EstadoTicket.Resuelto],
        [EstadoTicket.EnQa] = [EstadoTicket.EnPruebas, EstadoTicket.EnProceso],
        [EstadoTicket.EnPruebas] = [EstadoTicket.ValidacionCliente, EstadoTicket.EnProceso],
        [EstadoTicket.ValidacionCliente] = [EstadoTicket.Resuelto, EstadoTicket.EnProceso],
        [EstadoTicket.Resuelto] = [EstadoTicket.Cerrado, EstadoTicket.EnRevision],
        [EstadoTicket.Cerrado] = [],
        [EstadoTicket.Rechazado] = [],
        [EstadoTicket.Cancelado] = [],
    };

    /// <summary>Estados a los que la plataforma puede llevar el ticket.</summary>
    public static IReadOnlyList<EstadoTicket> Siguientes(EstadoTicket estado, TipoTicket tipo) =>
        Transiciones[estado]
            .Where(s => !(s == EstadoTicket.Resuelto && RequiereCalidad(tipo) && estado != EstadoTicket.ValidacionCliente))
            .ToList();

    /// <summary>Posición en el avance normal (para saber si un cambio es una devolución).</summary>
    private static int Etapa(EstadoTicket e) => e switch
    {
        EstadoTicket.Nuevo => 0,
        EstadoTicket.EnRevision or EstadoTicket.EsperandoCliente => 1,
        EstadoTicket.EnProceso => 2,
        EstadoTicket.EnQa => 3,
        EstadoTicket.EnPruebas => 4,
        EstadoTicket.ValidacionCliente => 5,
        EstadoTicket.Resuelto => 6,
        _ => 7,
    };

    /// <summary>Rechazar, pedir información, resolver o devolver a una etapa anterior exige explicar por qué.</summary>
    public static bool RequiereComentario(EstadoTicket desde, EstadoTicket hacia) =>
        hacia is EstadoTicket.Rechazado or EstadoTicket.EsperandoCliente or EstadoTicket.Resuelto || Etapa(hacia) < Etapa(desde);

    public static AccionesClienteDto AccionesCliente(EstadoTicket estado) => new(
        Comentar: !Finalizados.Contains(estado),
        Cancelar: !Finalizados.Contains(estado) && estado != EstadoTicket.Resuelto,
        Validar: estado == EstadoTicket.ValidacionCliente,
        Confirmar: estado == EstadoTicket.Resuelto,
        Reabrir: estado == EstadoTicket.Resuelto);

    public static string Nombre(EstadoTicket e) => e switch
    {
        EstadoTicket.Nuevo => "Nuevo",
        EstadoTicket.EnRevision => "En revisión",
        EstadoTicket.EsperandoCliente => "Esperando tu respuesta",
        EstadoTicket.EnProceso => "En proceso",
        EstadoTicket.EnQa => "Control de calidad (QA)",
        EstadoTicket.EnPruebas => "En pruebas",
        EstadoTicket.ValidacionCliente => "Pendiente de tu validación",
        EstadoTicket.Resuelto => "Resuelto",
        EstadoTicket.Cerrado => "Cerrado",
        EstadoTicket.Rechazado => "Rechazado",
        _ => "Cancelado",
    };

    public static string Nombre(TipoTicket t) => t switch
    {
        TipoTicket.Error => "Error",
        TipoTicket.Consulta => "Consulta",
        TipoTicket.Mejora => "Mejora",
        _ => "Capacitación",
    };

    public static string Nombre(PrioridadTicket p) => p switch
    {
        PrioridadTicket.Baja => "Baja",
        PrioridadTicket.Media => "Media",
        PrioridadTicket.Alta => "Alta",
        _ => "Crítica",
    };
}
