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
    /// <summary>
    /// Días de prueba que recibió la empresa al registrarse (el parámetro de la plataforma de ESE momento: si la
    /// plataforma lo cambia después, esta empresa conserva los suyos). 0 = sin prueba (pagó de una vez) o anterior.
    /// </summary>
    public int DiasPrueba { get; set; }
    /// <summary>
    /// La prueba o el plan pagado venció y el administrador aún no decide si paga o sigue en Gratis: al iniciar
    /// sesión ve la pasarela de pagos. Se apaga al pagar o al elegir Gratis.
    /// </summary>
    public bool PendienteDecision { get; set; }
    /// <summary>Plan que venció (para mostrar "Tu plan Profesional venció el …") y cuándo.</summary>
    public PlanSuscripcion? PlanVencido { get; set; }
    public DateTime? FechaVencimiento { get; set; }
    /// <summary>Último pago aprobado aplicado (UTC), para la tabla de la plataforma.</summary>
    public DateTime? FechaUltimoPago { get; set; }
}

/// <summary>
/// Parámetro global de la plataforma (no pertenece a una empresa), p. ej. <c>suscripcion.dias_prueba</c>.
/// Solo se aceptan las claves de <c>Application/Suscripciones/ParametrosPlataforma</c>.
/// </summary>
public class ParametroPlataforma : Entidad
{
    public string Clave { get; set; } = "";
    public string Valor { get; set; } = "";
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
    /// <summary>Código promocional usado (copia del texto, aunque luego se edite) y su descuento sobre el primer mes.</summary>
    public Guid? IdCodigoPromocional { get; set; }
    public string? CodigoPromocional { get; set; }
    public long DescuentoCentavos { get; set; }
}

/// <summary>
/// Código promocional que crea la plataforma. Descuenta SOLO el primer mes que se factura (en el anual, una de
/// sus mensualidades). Vigente entre <see cref="FechaInicio"/> y <see cref="FechaFin"/> (fechas de Colombia, ambas incluidas).
/// No pertenece a una empresa: <see cref="Alcance"/> y <see cref="Empresas"/> dicen a quién aplica.
/// </summary>
public class CodigoPromocional : Entidad
{
    /// <summary>En mayúsculas, único (A-Z, 0-9, guion).</summary>
    public string Codigo { get; set; } = "";
    public string? Descripcion { get; set; }
    public TipoDescuento TipoDescuento { get; set; }
    /// <summary>Porcentaje (1–100) o pesos.</summary>
    public decimal Valor { get; set; }
    public DateOnly FechaInicio { get; set; }
    public DateOnly FechaFin { get; set; }
    public AlcanceCodigo Alcance { get; set; }
    /// <summary>Planes a los que aplica separados por coma (<c>ESENCIAL,PROFESIONAL</c>); vacío = todos los de pago.</summary>
    public string Planes { get; set; } = "";
    /// <summary>null = mensual y anual.</summary>
    public PeriodoPago? Periodo { get; set; }
    /// <summary>Usos en total entre todas las empresas; null = sin límite.</summary>
    public int? UsosMaximos { get; set; }
    public int UsosPorEmpresa { get; set; } = 1;
    public bool Activo { get; set; } = true;
    public List<CodigoPromocionalEmpresa> Empresas { get; set; } = [];
}

/// <summary>Empresa a la que aplica un código con alcance EMPRESAS.</summary>
public class CodigoPromocionalEmpresa
{
    public Guid IdCodigoPromocional { get; set; }
    public CodigoPromocional CodigoPromocional { get; set; } = null!;
    public Guid IdEmpresa { get; set; }
}

/// <summary>
/// Un uso del código: se registra cuando el pago en línea queda aprobado o cuando la plataforma activa el plan a
/// mano con el código. Cuenta para los límites de usos.
/// </summary>
public class UsoCodigoPromocional : EntidadEmpresa
{
    public Guid IdCodigoPromocional { get; set; }
    public CodigoPromocional CodigoPromocional { get; set; } = null!;
    public Guid? IdPagoSuscripcion { get; set; }
    public PlanSuscripcion Plan { get; set; }
    public PeriodoPago Periodo { get; set; }
    public long DescuentoCentavos { get; set; }
}
