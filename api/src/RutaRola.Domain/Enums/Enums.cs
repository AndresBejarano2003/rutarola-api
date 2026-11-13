namespace RutaRola.Domain.Enums;

// Todos los enums se guardan como texto y viajan en JSON en UPPER_SNAKE_CASE
// (EnCurso → "EN_CURSO", ABordo → "A_BORDO"), igual que los tipos del frontend.

public enum AccionAuditoria { Crear, Actualizar, Eliminar }

public enum TemaApariencia { Oscuro, Claro }

// Personas
public enum TipoEmpleado { Conductor, Monitora }
public enum TipoContrato { TerminoIndefinido, TerminoFijo, ObraLabor, PrestacionServicios }

// Operación
public enum TipoVehiculo { Bus, Buseta, Microbus, Camioneta }
/// <summary>OTRO solo en el historial (extintor, revisión preventiva…): no tiene vencimiento en el vehículo.</summary>
public enum TipoDocumentoVehiculo { Soat, Tecnomecanica, TarjetaOperacion, Polizas, Otro }
/// <summary>RECOGIDA: casa → colegio. ENTREGA: colegio → casa (orden inverso).</summary>
public enum Trayecto { Recogida, Entrega }
public enum EstadoViaje { Programado, EnCurso, Finalizado, Cancelado }
/// <summary>PENDIENTE → A_BORDO → ENTREGADO, o PENDIENTE → AUSENTE.</summary>
public enum EstadoParada { Pendiente, ABordo, Entregado, Ausente }
/// <summary>
/// Por qué un estudiante quedó AUSENTE. NO_ASISTE: no va al colegio hoy (en la recogida; la entrega de ese
/// día ya lo trae como ausente). POR_SU_CUENTA: sí asiste pero no viaja en la ruta en ese trayecto.
/// </summary>
public enum MotivoAusencia { NoAsiste, PorSuCuenta }
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

// Soporte (tickets de las empresas a la plataforma)
public enum TipoTicket { Error, Consulta, Mejora, Capacitacion }
public enum PrioridadTicket { Baja, Media, Alta, Critica }
/// <summary>
/// NUEVO → EN_REVISION → EN_PROCESO → EN_QA → EN_PRUEBAS → VALIDACION_CLIENTE → RESUELTO → CERRADO, con
/// ESPERANDO_CLIENTE (falta información), RECHAZADO (no procede) y CANCELADO (lo retira el cliente).
/// </summary>
public enum EstadoTicket { Nuevo, EnRevision, EsperandoCliente, EnProceso, EnQa, EnPruebas, ValidacionCliente, Resuelto, Cerrado, Rechazado, Cancelado }
public enum TipoEventoTicket { Creado, CambioEstado, Comentario, NotaInterna, Asignacion, CambioPrioridad, Reunion, Calificacion }
public enum ModalidadReunion { Virtual, Presencial, Llamada }
public enum EstadoReunion { Programada, Realizada, Cancelada }

/// <summary>Plan contratado por la empresa (<c>Application/Suscripciones/CatalogoPlanes</c>).</summary>
public enum PlanSuscripcion { Gratis, Esencial, Profesional, Empresarial }
/// <summary>PRUEBA (días de Profesional al registrarse, parámetro de la plataforma), ACTIVA (pagada hasta <c>FechaFin</c>) o GRATIS (sin pago vigente).</summary>
public enum EstadoSuscripcion { Prueba, Activa, Gratis }
public enum PeriodoPago { Mensual, Anual }
/// <summary>Estado de un pago en línea (Wompi: PENDING, APPROVED, DECLINED, VOIDED, ERROR).</summary>
public enum EstadoPagoSuscripcion { Pendiente, Aprobado, Rechazado, Anulado, Error }
/// <summary>Descuento de un código promocional: PORCENTAJE (1–100 % del primer mes) o VALOR_FIJO (pesos, máximo un mes).</summary>
public enum TipoDescuento { Porcentaje, ValorFijo }
/// <summary>A quién aplica un código: TODAS las empresas, solo las EMPRESAS elegidas o la PRIMERA_COMPRA (nunca han pagado un plan).</summary>
public enum AlcanceCodigo { Todas, Empresas, PrimeraCompra }
