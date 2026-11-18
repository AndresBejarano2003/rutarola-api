using RutaRola.Domain.Comun;
using RutaRola.Domain.Comunicacion;
using RutaRola.Domain.Enums;
using RutaRola.Domain.Operacion;
using RutaRola.Domain.Personas;
using RutaRola.Domain.Seguridad;

namespace RutaRola.Domain.Finanzas;

/// <summary>
/// Contrato mensual de transporte de un estudiante entre dos meses. Los nombres se guardan como se
/// emitieron (snapshot). <see cref="ValorTotal"/> = valor mensual × meses.
/// </summary>
public class Factura : EntidadEmpresa
{
    /// <summary>FAC-0001, consecutivo por empresa generado por el servidor.</summary>
    public string NumeroFactura { get; set; } = "";
    public Guid IdAcudiente { get; set; }
    public Acudiente Acudiente { get; set; } = null!;
    public Guid IdEstudiante { get; set; }
    public Estudiante Estudiante { get; set; } = null!;
    public string NombreAcudiente { get; set; } = "";
    public string NombreEstudiante { get; set; } = "";
    /// <summary>Valor mensual (el frontend lo llama <c>valorContratacion</c>).</summary>
    public decimal ValorMensual { get; set; }
    /// <summary>Primer día del mes inicial (el API lo expone como <c>YYYY-MM</c>).</summary>
    public DateOnly PeriodoInicio { get; set; }
    public DateOnly PeriodoFin { get; set; }
    public string Concepto { get; set; } = "";
    public decimal ValorTotal { get; set; }
    public DateOnly FechaEmision { get; set; }
    public DateOnly? FechaVencimiento { get; set; }
    public EstadoFactura Estado { get; set; } = EstadoFactura.Vigente;
    public string? UrlPdf { get; set; }
    public string? Observaciones { get; set; }
    /// <summary>Cotización aprobada de la que salió (null = creada a mano).</summary>
    public Guid? IdCotizacion { get; set; }
    public Cotizacion? Cotizacion { get; set; }
    /// <summary>
    /// Retiro: primer mes que YA NO se cobra (el estudiante o el acudiente se retiró). Los meses desde aquí hasta
    /// <see cref="PeriodoFin"/> no generan cuota. Null = sin retiro.
    /// </summary>
    public DateOnly? PeriodoRetiro { get; set; }
    public string? MotivoRetiro { get; set; }
    public List<FacturaDetalle> Detalle { get; set; } = [];
}

/// <summary>Una línea por mes de la factura (generada por el servidor).</summary>
public class FacturaDetalle
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid IdFactura { get; set; }
    public Factura Factura { get; set; } = null!;
    public DateOnly? Periodo { get; set; }
    public string Descripcion { get; set; } = "";
    public int Cantidad { get; set; } = 1;
    public decimal PrecioUnitario { get; set; }
    public decimal Total { get; set; }
}

/// <summary>Recibo de pago de una cuota mensual (recurso <c>/recibos</c> del frontend).</summary>
public class Pago : EntidadEmpresa
{
    /// <summary>REC-0001, consecutivo por empresa.</summary>
    public string Numero { get; set; } = "";
    public Guid IdEstudiante { get; set; }
    public Estudiante Estudiante { get; set; } = null!;
    /// <summary>Nullable en BD (pagos sueltos a futuro); obligatorio en <c>/recibos</c>.</summary>
    public Guid? IdFactura { get; set; }
    public Factura? Factura { get; set; }
    /// <summary>Primer mes que paga (primer día del mes); los meses y valores aplicados están en <see cref="Detalle"/>.</summary>
    public DateOnly? Periodo { get; set; }
    public decimal Valor { get; set; }
    public DateOnly FechaPago { get; set; }
    public MetodoPago MetodoPago { get; set; }
    public EstadoPago Estado { get; set; } = EstadoPago.Vigente;
    public string? NumeroReferencia { get; set; }
    // Snapshot
    public string? NumeroFactura { get; set; }
    public string NombreAcudiente { get; set; } = "";
    public string NombreEstudiante { get; set; } = "";
    public string? Observaciones { get; set; }
    /// <summary>Cuánto de este recibo se aplicó a cada mes de la factura (un recibo puede pagar o abonar varios).</summary>
    public List<PagoDetalle> Detalle { get; set; } = [];
}

/// <summary>Valor de un recibo aplicado a un mes de la factura (pago completo o abono).</summary>
public class PagoDetalle
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid IdPago { get; set; }
    public Pago Pago { get; set; } = null!;
    /// <summary>Primer día del mes.</summary>
    public DateOnly Periodo { get; set; }
    public decimal Valor { get; set; }
}

public class Cotizacion : EntidadEmpresa
{
    /// <summary>COT-0001, consecutivo por empresa.</summary>
    public string Numero { get; set; } = "";
    public DateOnly Fecha { get; set; }
    /// <summary>Acudiente ya registrado (si no, solo nombre y teléfono).</summary>
    public Guid? IdAcudiente { get; set; }
    public Acudiente? Acudiente { get; set; }
    public string NombreAcudiente { get; set; } = "";
    public string? TelefonoAcudiente { get; set; }
    /// <summary>Meses del servicio cotizado (primer día de cada mes).</summary>
    public DateOnly PeriodoInicio { get; set; }
    public DateOnly PeriodoFin { get; set; }
    /// <summary>Suma de los valores mensuales de los estudiantes.</summary>
    public decimal ValorMensual { get; set; }
    /// <summary><see cref="ValorMensual"/> × meses del servicio.</summary>
    public decimal ValorTotal { get; set; }
    public EstadoCotizacion Estado { get; set; } = EstadoCotizacion.Pendiente;
    /// <summary>Cuándo se convirtió en facturas (null = aún no).</summary>
    public DateTime? FechaFacturacion { get; set; }
    public string? Observaciones { get; set; }
    public List<CotizacionEstudiante> Estudiantes { get; set; } = [];
}

public class CotizacionEstudiante
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid IdCotizacion { get; set; }
    public Cotizacion Cotizacion { get; set; } = null!;
    public Guid? IdEstudiante { get; set; }
    public Estudiante? Estudiante { get; set; }
    public string Nombre { get; set; } = "";
    /// <summary>Colegio registrado (null = solo el nombre escrito, cliente nuevo).</summary>
    public Guid? IdColegio { get; set; }
    public Colegio? Colegio { get; set; }
    public string NombreColegio { get; set; } = "";
    /// <summary>Valor mensual cotizado para este estudiante.</summary>
    public decimal Valor { get; set; }
    public int Orden { get; set; }
}

/// <summary>
/// Gasto de un vehículo (tanqueada, mantenimiento, repuesto, llantas, peajes…): historial y tablero en
/// Operación › Vehículos. <see cref="Cantidad"/> = galones en combustible (unidades en lo demás).
/// </summary>
public class Gasto : EntidadEmpresa
{
    public Guid? IdVehiculo { get; set; }
    public Vehiculo? Vehiculo { get; set; }
    public TipoGasto Tipo { get; set; }
    public string? Descripcion { get; set; }
    public decimal Valor { get; set; }
    public DateOnly FechaGasto { get; set; }
    public string? Proveedor { get; set; }
    /// <summary>Número de factura o comprobante.</summary>
    public string? Referencia { get; set; }
    public int? Kilometraje { get; set; }
    public decimal? Cantidad { get; set; }
    public Guid? IdArchivo { get; set; }
    public Archivo? Archivo { get; set; }
    public Guid? CreadoPor { get; set; }
    public Usuario? Creador { get; set; }
}

/// <summary>Recordatorios automáticos de cobro de una factura (los envía el servidor).</summary>
public class ProgramacionCobro : EntidadEmpresa
{
    public string Nombre { get; set; } = "";
    public Guid IdFactura { get; set; }
    public Factura Factura { get; set; } = null!;
    public int DiaLimitePago { get; set; } = 5;
    public int DiasPrevios { get; set; } = 3;
    public bool Whatsapp { get; set; }
    public bool Email { get; set; }
    public bool Activo { get; set; } = true;
    public List<PlantillaMensaje> Plantillas { get; set; } = [];
    public List<EnvioProgramacion> Envios { get; set; } = [];
}

public class PlantillaMensaje
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid IdProgramacion { get; set; }
    public ProgramacionCobro Programacion { get; set; } = null!;
    public CanalEnvio Canal { get; set; }
    public string? Asunto { get; set; }
    public string Cuerpo { get; set; } = "";
    public EstadoPlantilla Estado { get; set; } = EstadoPlantilla.Activo;
    public int Orden { get; set; }
    public List<PlantillaMensajeArchivo> Adjuntos { get; set; } = [];
}

public class PlantillaMensajeArchivo
{
    public Guid IdPlantilla { get; set; }
    public PlantillaMensaje Plantilla { get; set; } = null!;
    public Guid IdArchivo { get; set; }
    public Archivo Archivo { get; set; } = null!;
}

public class EnvioProgramacion
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid IdProgramacion { get; set; }
    public ProgramacionCobro Programacion { get; set; } = null!;
    public DateTime Fecha { get; set; }
    public CanalEnvio Canal { get; set; }
    public string Destinatario { get; set; } = "";
    public DateOnly Periodo { get; set; }
    public EstadoEnvio Estado { get; set; }
    public string? Detalle { get; set; }
    public Guid? IdMensajeWhatsapp { get; set; }
    public MensajeWhatsapp? MensajeWhatsapp { get; set; }
}
