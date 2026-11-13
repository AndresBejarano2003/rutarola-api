using RutaRola.Domain.Enums;

namespace RutaRola.Application.Suscripciones;

/// <summary>Funciones que dependen del plan (se validan en el servidor).</summary>
public static class FuncionPlan
{
    /// <summary>Teléfonos del conductor/monitora comparten la ubicación; monitoreo y mapa del acudiente.</summary>
    public const string GpsEnVivo = "GPS_EN_VIVO";
    /// <summary>El servidor envía los recordatorios de cobro por correo/WhatsApp.</summary>
    public const string Recordatorios = "RECORDATORIOS";
    public const string SoportePrioritario = "SOPORTE_PRIORITARIO";
    public const string Acompanamiento = "ACOMPANAMIENTO";
    /// <summary>Reportes de ingresos, cartera y morosos (<c>GET /reportes/*</c>).</summary>
    public const string Reportes = "REPORTES";

    /// <summary>Nombre para los mensajes ("Tu plan Gratis no incluye …").</summary>
    public static string Nombre(string funcion) => funcion switch
    {
        GpsEnVivo => "el GPS en vivo",
        Recordatorios => "los recordatorios automáticos de cobro",
        Reportes => "los reportes financieros",
        SoportePrioritario => "el soporte prioritario",
        _ => "esta función",
    };
}

/// <summary>
/// Un plan. <c>null</c> en un límite = sin límite. Precios en pesos (COP) con IVA incluido; el anual equivale a
/// 10 mensualidades (2 meses gratis).
/// </summary>
public record DefinicionPlan(
    PlanSuscripcion Plan, string Nombre, string Descripcion, long PrecioMensual, int? Vehiculos, int? Estudiantes,
    IReadOnlyList<string> Funciones, IReadOnlyList<string> Incluye, bool Destacado = false, bool PrecioDesde = false)
{
    public long PrecioAnual => PrecioMensual * CatalogoPlanes.MesesPagadosPorAnio;

    public long Precio(PeriodoPago periodo) => periodo == PeriodoPago.Anual ? PrecioAnual : PrecioMensual;

    public bool Tiene(string funcion) => Funciones.Contains(funcion);
}

/// <summary>
/// Planes de RutaRola (mismo catálogo en <c>sistema/membresias/planes.ts</c>, que lo lee de <c>GET /suscripcion/planes</c>).
/// Quien se registra recibe los días de prueba de <see cref="PlanPrueba"/> que diga la plataforma
/// (<see cref="ServicioParametrosPlataforma.DiasPrueba"/>); al terminar sin pagar elige pagar o pasar a Gratis.
/// </summary>
public static class CatalogoPlanes
{
    public const PlanSuscripcion PlanPrueba = PlanSuscripcion.Profesional;
    public const int MesesPagadosPorAnio = 10;

    private static readonly string[] Base =
    [
        "Ruta del día para conductor y monitora con asistencia por estudiante",
        "Estudiantes, acudientes, colegios, rutas y vehículos",
        "Facturas, recibos y estado de cuenta",
        "Portal del acudiente con sus pagos y estudiantes",
        "Alertas de documentos y licencias por vencer",
        "Tablero con indicadores del mes",
    ];

    public static readonly IReadOnlyList<DefinicionPlan> Todos =
    [
        new(PlanSuscripcion.Gratis, "Gratis", "Para empezar a organizar una ruta sin costo.", 0, 1, 20, [],
            [.. Base, "Soporte por tickets"]),
        new(PlanSuscripcion.Esencial, "Esencial", "Para pequeñas empresas que quieren cobrar a tiempo.", 89_900, 5, 120,
            [FuncionPlan.Recordatorios, FuncionPlan.Reportes],
            ["Todo lo del plan Gratis", "Recordatorios automáticos de cobro por correo y WhatsApp", "Reportes de ingresos, cartera y morosos con exportación a CSV"]),
        new(PlanSuscripcion.Profesional, "Profesional", "Para empresas en crecimiento que quieren dar tranquilidad a las familias.", 189_900, 15, 400,
            [FuncionPlan.Recordatorios, FuncionPlan.Reportes, FuncionPlan.GpsEnVivo, FuncionPlan.SoportePrioritario],
            ["Todo lo del plan Esencial", "GPS en vivo desde los teléfonos del conductor y la monitora", "Mapa del recorrido para los acudientes", "Monitoreo de todas las rutas en tiempo real e historial con trayecto", "Soporte prioritario"],
            Destacado: true),
        new(PlanSuscripcion.Empresarial, "Empresarial", "Para flotas grandes o varias sedes.", 389_900, null, null,
            [FuncionPlan.Recordatorios, FuncionPlan.Reportes, FuncionPlan.GpsEnVivo, FuncionPlan.SoportePrioritario, FuncionPlan.Acompanamiento],
            ["Todo lo del plan Profesional", "Vehículos y estudiantes sin límite", "Capacitación del equipo y acompañamiento en la puesta en marcha", "Atención de incidentes críticos en 8 horas"],
            PrecioDesde: true),
    ];

    public static DefinicionPlan De(PlanSuscripcion plan) => Todos.First(p => p.Plan == plan);

    /// <summary>El primer plan (el más barato) que incluye la función.</summary>
    public static DefinicionPlan DesdeConFuncion(string funcion) => Todos.First(p => p.Tiene(funcion));
}
