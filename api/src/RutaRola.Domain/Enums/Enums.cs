namespace RutaRola.Domain.Enums;

// Todos los enums se guardan como texto y viajan en JSON en UPPER_SNAKE_CASE
// (EnCurso → "EN_CURSO", ABordo → "A_BORDO"), igual que los tipos del frontend.

public enum AccionAuditoria { Crear, Actualizar, Eliminar }

// Personas
public enum TipoEmpleado { Conductor, Monitora }
public enum TipoContrato { TerminoIndefinido, TerminoFijo, ObraLabor, PrestacionServicios }

// Operación
public enum TipoVehiculo { Bus, Buseta, Microbus, Camioneta }
public enum TipoDocumentoVehiculo { Soat, Tecnomecanica, TarjetaOperacion, Polizas }
/// <summary>RECOGIDA: casa → colegio. ENTREGA: colegio → casa (orden inverso).</summary>
public enum Trayecto { Recogida, Entrega }
public enum EstadoViaje { Programado, EnCurso, Finalizado, Cancelado }
/// <summary>PENDIENTE → A_BORDO → ENTREGADO, o PENDIENTE → AUSENTE.</summary>
public enum EstadoParada { Pendiente, ABordo, Entregado, Ausente }
public enum TipoEvento { Inicio, CambioParada, Reorden, Novedad, Finalizacion, Cancelacion }

// Finanzas
public enum EstadoFactura { Vigente, Anulada }
public enum MetodoPago { Efectivo, Transferencia, Nequi, Daviplata, Tarjeta }
public enum EstadoPago { Vigente, Anulado }
public enum EstadoCotizacion { Pendiente, Aprobada, Rechazada }
public enum CanalEnvio { Whatsapp, Email }
public enum EstadoPlantilla { EnUso, Activo, Inactivo }
public enum EstadoEnvio { Enviado, Fallido }

// Comunicación
public enum CategoriaNotificacion { Pagos, Facturacion, Cotizaciones, Vencimientos, Operacion, Personas, Envios, Sistema }
public enum NivelNotificacion { Critica, Advertencia, Informativa, Exito }
public enum EstadoMensaje { Pendiente, Enviado, Entregado, Leido, Fallido }
