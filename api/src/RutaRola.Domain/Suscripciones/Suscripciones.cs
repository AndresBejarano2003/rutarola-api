using RutaRola.Domain.Comun;
using RutaRola.Domain.Enums;

namespace RutaRola.Domain.Suscripciones;

/// <summary>
/// Plan vigente de una empresa (una fila por empresa). Al vencerse la prueba o el periodo pagado la empresa
/// pasa al plan Gratis: conserva sus datos y solo se limita lo que crea de nuevo.
/// </summary>
public class Suscripcion : EntidadEmpresa
{
    public PlanSuscripcion Plan { get; set; }
    public EstadoSuscripcion Estado { get; set; }
    public PeriodoPago? Periodo { get; set; }
    /// <summary>UTC. Inicio de la prueba o del periodo actual.</summary>
    public DateTime FechaInicio { get; set; }
    /// <summary>UTC. Fin de la prueba o del periodo pagado; null en Gratis.</summary>
    public DateTime? FechaFin { get; set; }
    /// <summary>Si ya usó la prueba gratuita (no se repite).</summary>
    public bool PruebaUsada { get; set; }
    /// <summary>Nota de la plataforma cuando ajusta el plan a mano (convenio, transferencia…).</summary>
    public string? Nota { get; set; }
}

/// <summary>
/// Intento de pago en línea (Wompi). La referencia es única; el plan se activa SOLO cuando Wompi confirma
/// el pago aprobado (evento firmado o consulta al API de Wompi), nunca por la redirección del navegador.
/// </summary>
public class PagoSuscripcion : EntidadEmpresa
{
    public string Referencia { get; set; } = "";
    public PlanSuscripcion Plan { get; set; }
    public PeriodoPago Periodo { get; set; }
    public long ValorCentavos { get; set; }
    public EstadoPagoSuscripcion Estado { get; set; } = EstadoPagoSuscripcion.Pendiente;
    public string? IdTransaccion { get; set; }
    public string? MetodoPago { get; set; }
    /// <summary>"test" o "prod".</summary>
    public string? Ambiente { get; set; }
    public Guid? IdUsuario { get; set; }
    public string? CorreoPagador { get; set; }
    /// <summary>UTC. Cuando se aplicó al plan (idempotencia).</summary>
    public DateTime? FechaAplicacion { get; set; }
}
