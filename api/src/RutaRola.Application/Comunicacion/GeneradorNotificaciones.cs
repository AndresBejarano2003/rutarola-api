using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Application.Configuracion;
using Microsoft.Extensions.Logging;
using RutaRola.Application.Finanzas;
using RutaRola.Application.Suscripciones;
using RutaRola.Domain.Comunicacion;
using RutaRola.Domain.Enums;
using RutaRola.Domain.Seguridad;

namespace RutaRola.Application.Comunicacion;

/// <summary>Hecho a notificar (<c>NuevaNotificacion</c> del frontend). <c>Fecha</c> null = cuando se detecta.</summary>
public record NuevaNotificacion(
    string Clave, CategoriaNotificacion Categoria, NivelNotificacion Nivel, string Titulo, string Mensaje, string Enlace, string TextoEnlace,
    DateTime? Fecha = null);

/// <summary>
/// Crea las notificaciones de los administradores de la empresa del contexto con las reglas de
/// <c>generar-notificaciones.ts</c>: eventos de los últimos <see cref="VentanaEventosDias"/> días (pagos,
/// facturas, cotizaciones, vehículos, empleados, envíos) y condiciones del día (documentos y licencias por
/// vencer o vencidos, contratos por terminar o terminados, mora, y la membresía: por vencer desde los días que fija la
/// plataforma, último día y vencida → Gratis). Cada hecho tiene una <c>clave</c> y se notifica una sola vez por usuario,
/// aunque la elimine (índice único usuario + clave). Los de membresía también llegan por correo a cada administrador.
/// </summary>
public class GeneradorNotificaciones(
    IBaseDatos db, ServicioReportes reportes, ParametrosEmpresa parametros, TimeProvider reloj, ICanalCorreo correo,
    ILogger<GeneradorNotificaciones> logger)
{
    /// <summary>Prefijo de las claves de membresía (también se envían por correo).</summary>
    public const string PrefijoMembresia = "membresia:";

    public const int VentanaEventosDias = 30;

    /// <summary>Genera y guarda las notificaciones nuevas. Devuelve cuántas creó (sumando todos los administradores).</summary>
    public async Task<int> GenerarAsync(CancellationToken ct = default)
    {
        var usuarios = await db.Usuarios.AsNoTracking()
            .Where(u => u.Activo && u.IdEmpresa == db.IdEmpresaActual && u.Rol.Nombre == Roles.AdminEmpresa)
            .Select(u => new { u.Id, u.Email }).ToListAsync(ct);
        var destinatarios = usuarios.Select(u => u.Id).ToList();
        if (destinatarios.Count == 0)
        {
            return 0;
        }

        var nuevas = await CalcularAsync(ct);
        var claves = nuevas.Select(n => n.Clave).ToList();
        var existentes = (await db.Notificaciones.AsNoTracking()
                .Where(n => destinatarios.Contains(n.IdUsuario) && claves.Contains(n.Clave))
                .Select(n => new { n.IdUsuario, n.Clave }).ToListAsync(ct))
            .Select(n => (n.IdUsuario, n.Clave)).ToHashSet();

        var ahora = reloj.GetUtcNow().UtcDateTime;
        var creadas = 0;
        var correos = new List<(string Destino, NuevaNotificacion Notificacion)>();
        foreach (var usuario in destinatarios)
        {
            foreach (var n in nuevas.Where(n => !existentes.Contains((usuario, n.Clave))))
            {
                if (n.Clave.StartsWith(PrefijoMembresia, StringComparison.Ordinal)
                    && Texto.Opcional(usuarios.First(u => u.Id == usuario).Email) is { } destino)
                {
                    correos.Add((destino, n));
                }
                db.Notificaciones.Add(new Notificacion
                {
                    IdUsuario = usuario, Clave = n.Clave, Categoria = n.Categoria, Nivel = n.Nivel, Titulo = n.Titulo, Mensaje = n.Mensaje,
                    Enlace = n.Enlace, TextoEnlace = n.TextoEnlace, FechaCreacion = n.Fecha ?? ahora,
                });
                creadas++;
            }
        }
        if (creadas > 0)
        {
            await db.SaveChangesAsync(ct);
        }
        foreach (var (destino, n) in correos)
        {
            await EnviarCorreoAsync(destino, n, ct);
        }
        return creadas;
    }

    /// <summary>Correo del aviso de membresía (nunca falla la generación).</summary>
    private async Task EnviarCorreoAsync(string destino, NuevaNotificacion n, CancellationToken ct)
    {
        if (!correo.Habilitado) return;
        try
        {
            var resultado = await correo.EnviarAsync(destino, $"RutaRola: {n.Titulo}",
                $"{n.Mensaje}\n\nRevisa tu plan en RutaRola › Sistema › Planes y membresías.", [], ct);
            if (!resultado.Exito)
            {
                logger.LogWarning("No se pudo enviar el aviso de membresía: {Detalle}", resultado.Detalle);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Falló el envío del aviso de membresía");
        }
    }

    /// <summary>
    /// Avisos de la membresía (iguales a <c>avisosMembresia</c> de <c>generar-notificaciones.ts</c>): por vencer desde
    /// <paramref name="diasAviso"/> días antes del último día, el último día, y vencida (ya en Gratis) durante
    /// <see cref="VentanaEventosDias"/> días. El plan dura hasta el final de su último día.
    /// </summary>
    public static IEnumerable<NuevaNotificacion> AvisosMembresia(Domain.Suscripciones.Suscripcion? s, DateOnly hoy, int diasAviso, DateTime ahora)
    {
        if (s is null)
        {
            yield break;
        }
        const string Enlace = "/sistema/membership";
        if (s.Estado != EstadoSuscripcion.Gratis && s.FechaFin is { } fin && !ServicioSuscripcion.VencidaEn(s, ahora))
        {
            var ultimo = ServicioSuscripcion.DiaFin(fin);
            var dias = ultimo.DayNumber - hoy.DayNumber;
            var prueba = s.Estado == EstadoSuscripcion.Prueba;
            var nombre = CatalogoPlanes.De(s.Plan).Nombre;
            var que = prueba ? $"Tu prueba gratuita del plan {nombre}" : $"Tu plan {nombre}";
            var consecuencia = $"Si no pagas, desde el {Fechas.Corta(ultimo.AddDays(1))} quedas en el plan Gratis: se desactivan las funciones de tu plan " +
                "y se inactivan los vehículos y estudiantes que superen sus límites (no se borra nada).";
            var clave = $"{PrefijoMembresia}{TextoEnum.Escribir(s.Estado).ToLowerInvariant()}:{ultimo:yyyy-MM-dd}";
            if (dias == 0)
            {
                yield return new($"{clave}:ultimo-dia", CategoriaNotificacion.Sistema, NivelNotificacion.Critica, $"{que} vence hoy",
                    $"{que} termina hoy {Fechas.Corta(ultimo)}. {consecuencia}", Enlace, "Ver planes");
            }
            else if (dias <= diasAviso)
            {
                yield return new($"{clave}:por-vencer", CategoriaNotificacion.Sistema, NivelNotificacion.Advertencia,
                    $"{que} vence en {Plural(dias, "día", "días")}",
                    $"{que} termina el {Fechas.Corta(ultimo)}. {consecuencia}", Enlace, "Ver planes");
            }
        }
        if (s.Estado == EstadoSuscripcion.Gratis && s.PlanVencido is { } vencido && s.FechaVencimiento is { } vencio)
        {
            var ultimo = ServicioSuscripcion.DiaFin(vencio);
            if (hoy.DayNumber - ultimo.DayNumber <= VentanaEventosDias)
            {
                var nombre = CatalogoPlanes.De(vencido).Nombre;
                yield return new($"{PrefijoMembresia}vencida:{TextoEnum.Escribir(vencido).ToLowerInvariant()}:{ultimo:yyyy-MM-dd}",
                    CategoriaNotificacion.Sistema, NivelNotificacion.Critica, $"Tu plan {nombre} venció: ahora estás en Gratis",
                    $"Tu plan {nombre} terminó el {Fechas.Corta(ultimo)} sin pago y desde el {Fechas.Corta(ultimo.AddDays(1))} estás en el plan Gratis. " +
                    "Los vehículos y estudiantes que superaban sus límites quedaron inactivos (no se borró nada). Paga un plan para recuperar sus funciones.",
                    Enlace, "Mejorar mi plan");
            }
        }
    }

    /// <summary>Los hechos notificables de hoy (sin consultar qué se notificó antes).</summary>
    public async Task<IReadOnlyList<NuevaNotificacion>> CalcularAsync(CancellationToken ct = default)
    {
        var hoy = Fechas.Hoy(reloj);
        var desde = DateTime.SpecifyKind(hoy.AddDays(-VentanaEventosDias).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc) - Fechas.DesfaseColombia;
        var mesActual = Periodos.De(hoy);
        var diasAlerta = await parametros.DiasAlertaVencimientoAsync(ct);
        var resultado = new List<NuevaNotificacion>();

        // Membresía (días de aviso: parámetro de la plataforma)
        var suscripcion = await db.Suscripciones.AsNoTracking().FirstOrDefaultAsync(ct);
        resultado.AddRange(AvisosMembresia(suscripcion, hoy, await ServicioParametrosPlataforma.DiasAvisoVencimientoAsync(db, ct),
            reloj.GetUtcNow().UtcDateTime));

        // Pagos recibidos
        var recibos = await db.Pagos.AsNoTracking().Include(p => p.Detalle)
            .Where(p => p.Estado == EstadoPago.Vigente && p.IdFactura != null && p.FechaCreacion >= desde).ToListAsync(ct);
        foreach (var r in recibos)
        {
            resultado.Add(new($"recibo:{r.Id}", CategoriaNotificacion.Pagos, NivelNotificacion.Exito, $"Pago recibido · {r.Numero}",
                $"{r.NombreAcudiente} pagó {Cobro.Pesos(r.Valor)} de {MesesRecibo(r)} por {r.NombreEstudiante} " +
                $"(factura {r.NumeroFactura}). Medio de pago: {NombreMedio(r.MetodoPago)}.",
                "/finanzas/recibos", "Ver recibos", r.FechaCreacion));
        }

        // Novedades del vehículo reportadas por el conductor o la monitora (las que siguen abiertas)
        var novedades = await db.NovedadesVehiculo.AsNoTracking().Include(n => n.Vehiculo)
            .Where(n => n.IdEmpleado != null && n.Estado != EstadoNovedadVehiculo.Resuelta && n.FechaCreacion >= desde).ToListAsync(ct);
        foreach (var n in novedades)
        {
            resultado.Add(new($"novedad-vehiculo:{n.Id}", CategoriaNotificacion.Operacion,
                n.Gravedad == GravedadNovedad.Alta ? NivelNotificacion.Critica : NivelNotificacion.Advertencia,
                $"Novedad en el vehículo {n.Vehiculo.Placa} · {NombreNovedad(n.Tipo)}",
                $"{n.NombreReporta} reportó: {Recortar(n.Descripcion, 160)}" +
                (n.Gravedad == GravedadNovedad.Alta ? " Gravedad alta: revisa si el vehículo puede salir." : ""),
                $"/operacion/vehiculos?novedad={n.Id}", "Ver novedad", n.FechaCreacion));
        }

        // Acudientes en mora (la clave cambia si se vence otra cuota → nueva notificación)
        foreach (var m in await reportes.MorososAsync(new FiltrosMorosos(), ct))
        {
            resultado.Add(new($"mora:{m.AcudienteId}:{string.Join(',', m.Periodos)}", CategoriaNotificacion.Pagos, NivelNotificacion.Critica,
                $"Acudiente en mora · {m.NombreAcudiente}",
                $"{m.NombreAcudiente} debe {Plural(m.Cuotas, "cuota", "cuotas")} por {Cobro.Pesos(m.ValorAdeudado)} " +
                $"({string.Join(", ", m.Periodos.Select(p => Fechas.MesCorto(Periodos.Leer(p))))}). La más antigua tiene {m.DiasMoraMaxima} días de mora.",
                "/finanzas/reportes-financieros/morosos", "Ver reporte de morosos"));
        }

        // Facturas nuevas y contratos por terminar / terminados
        var facturas = await db.Facturas.AsNoTracking()
            .Where(f => f.FechaCreacion >= desde || (f.Estado == EstadoFactura.Vigente && f.PeriodoFin <= mesActual.AddMonths(1)))
            .ToListAsync(ct);
        foreach (var f in facturas)
        {
            if (f.FechaCreacion >= desde)
            {
                resultado.Add(new($"factura:{f.Id}", CategoriaNotificacion.Facturacion, NivelNotificacion.Informativa, $"Nueva factura · {f.NumeroFactura}",
                    $"{f.NombreAcudiente} — {f.NombreEstudiante}: {Cobro.Pesos(f.ValorMensual)} mensuales de {Periodos.Nombre(f.PeriodoInicio, true)} " +
                    $"a {Periodos.Nombre(f.PeriodoFin, true)}.",
                    "/finanzas/facturas", "Ver facturas", f.FechaCreacion));
            }
            // Con retiro el fin es una decisión ya tomada: no se avisa como contrato por terminar.
            if (f.Estado != EstadoFactura.Vigente || f.PeriodoRetiro is not null)
            {
                continue;
            }
            var fin = Periodos.Texto(f.PeriodoFin);
            if (f.PeriodoFin == mesActual || f.PeriodoFin == mesActual.AddMonths(1))
            {
                resultado.Add(new($"factura-termina:{f.Id}:{fin}", CategoriaNotificacion.Facturacion, NivelNotificacion.Advertencia,
                    $"Contrato por terminar · {f.NumeroFactura}",
                    $"El contrato de {f.NombreEstudiante} (acudiente {f.NombreAcudiente}) termina en {Periodos.Nombre(f.PeriodoFin, true)}. " +
                    "Si el servicio continúa, crea una nueva factura para los meses siguientes.",
                    "/finanzas/facturas", "Ver facturas"));
            }
            else if (f.PeriodoFin < mesActual)
            {
                resultado.Add(new($"factura-terminada:{f.Id}:{fin}", CategoriaNotificacion.Facturacion, NivelNotificacion.Informativa,
                    $"Contrato terminado · {f.NumeroFactura}",
                    $"El contrato de {f.NombreEstudiante} (acudiente {f.NombreAcudiente}) terminó en {Periodos.Nombre(f.PeriodoFin, true)} y la factura " +
                    "sigue vigente. Crea una nueva si el servicio continúa o cámbiala a Inactiva.",
                    "/finanzas/facturas", "Ver facturas"));
            }
        }

        // Cotizaciones nuevas y su respuesta
        var cotizaciones = await db.Cotizaciones.AsNoTracking().Include(c => c.Estudiantes)
            .Where(c => c.FechaCreacion >= desde || (c.Estado != EstadoCotizacion.Pendiente && c.FechaActualizacion >= desde))
            .ToListAsync(ct);
        foreach (var c in cotizaciones)
        {
            var estudiantes = Plural(c.Estudiantes.Count, "estudiante", "estudiantes");
            if (c.FechaCreacion >= desde)
            {
                resultado.Add(new($"cotizacion:{c.Id}", CategoriaNotificacion.Cotizaciones, NivelNotificacion.Informativa, $"Nueva cotización · {c.Numero}",
                    $"{c.NombreAcudiente}: {estudiantes} por {Cobro.Pesos(c.ValorTotal)} en total.", "/finanzas/cotizaciones", "Ver cotizaciones",
                    c.FechaCreacion));
            }
            if (c.Estado != EstadoCotizacion.Pendiente && c.FechaActualizacion >= desde)
            {
                var aprobada = c.Estado == EstadoCotizacion.Aprobada;
                resultado.Add(new($"cotizacion-{(aprobada ? "aprobada" : "rechazada")}:{c.Id}", CategoriaNotificacion.Cotizaciones,
                    aprobada ? NivelNotificacion.Exito : NivelNotificacion.Informativa,
                    $"Cotización {(aprobada ? "aprobada" : "rechazada")} · {c.Numero}",
                    aprobada
                        ? $"{c.NombreAcudiente} aprobó la cotización ({estudiantes}, {Cobro.Pesos(c.ValorTotal)}). Siguiente paso: registrar la factura."
                        : $"{c.NombreAcudiente} rechazó la cotización ({estudiantes}, {Cobro.Pesos(c.ValorTotal)}).",
                    aprobada ? "/finanzas/facturas" : "/finanzas/cotizaciones", aprobada ? "Crear factura" : "Ver cotizaciones", c.FechaActualizacion));
            }
        }

        // Vehículos: registro y documentos
        var vehiculos = await db.Vehiculos.AsNoTracking().Include(v => v.Documentos).Where(v => v.Activo || v.FechaCreacion >= desde).ToListAsync(ct);
        foreach (var v in vehiculos)
        {
            if (v.FechaCreacion >= desde)
            {
                resultado.Add(new($"vehiculo:{v.Id}", CategoriaNotificacion.Operacion, NivelNotificacion.Informativa, $"Vehículo registrado · {v.Placa}",
                    $"{NombreTipo(v.Tipo)} {v.Marca} {v.Linea} modelo {v.Anio}, interno {v.NumeroInterno}, con capacidad para {v.Capacidad} pasajeros.",
                    "/operacion/vehiculos", "Ver vehículos", v.FechaCreacion));
            }
            if (!v.Activo)
            {
                continue;
            }
            foreach (var d in v.Documentos.OrderBy(d => d.TipoDocumento))
            {
                var estado = Catalogos.EstadoVencimiento(d.FechaVencimiento, hoy, diasAlerta);
                if (estado is not ("POR_VENCER" or "VENCIDA"))
                {
                    continue;
                }
                var (campo, nombre, femenino, plural) = Documento(d.TipoDocumento);
                var vencido = estado == "VENCIDA";
                var palabra = vencido ? $"vencid{(femenino ? "a" : "o")}{(plural ? "s" : "")}" : "por vencer";
                var dias = Math.Abs(d.FechaVencimiento.DayNumber - hoy.DayNumber);
                var fecha = Fechas.Corta(d.FechaVencimiento);
                resultado.Add(new($"veh-doc:{v.Id}:{campo}:{d.FechaVencimiento:yyyy-MM-dd}:{estado}", CategoriaNotificacion.Vencimientos,
                    vencido ? NivelNotificacion.Critica : NivelNotificacion.Advertencia, $"{nombre} {palabra} · {v.Placa}",
                    vencido
                        ? $"{nombre} del vehículo {v.Placa} (interno {v.NumeroInterno}) {(plural ? "vencieron" : "venció")} el {fecha}, hace {Plural(dias, "día", "días")}. " +
                          "Un vehículo con documentos vencidos no se puede asignar a rutas."
                        : $"{nombre} del vehículo {v.Placa} (interno {v.NumeroInterno}) {(plural ? "vencen" : "vence")} el {fecha}, en {Plural(dias, "día", "días")}. " +
                          "Renuévalo a tiempo para no afectar las rutas.",
                    "/operacion/vehiculos", "Ver vehículos"));
            }
        }

        // Empleados: registro y licencias de conductores
        var empleados = await db.Empleados.AsNoTracking()
            .Where(e => e.FechaCreacion >= desde || (e.Activo && e.TipoEmpleado == TipoEmpleado.Conductor)).ToListAsync(ct);
        foreach (var e in empleados)
        {
            if (e.FechaCreacion >= desde)
            {
                resultado.Add(new($"empleado:{e.Id}", CategoriaNotificacion.Personas, NivelNotificacion.Informativa, $"Empleado registrado · {e.NombreCompleto}",
                    $"{e.NombreCompleto} ({e.TipoDocumento} {e.NumeroDocumento}) fue registrado como {(e.TipoEmpleado == TipoEmpleado.Conductor ? "conductor" : "monitora")}.",
                    "/personas/empleados", "Ver empleados", e.FechaCreacion));
            }
            if (!e.Activo || e.TipoEmpleado != TipoEmpleado.Conductor || e.FechaVencimientoLicencia is not { } vence)
            {
                continue;
            }
            var estado = Catalogos.EstadoVencimiento(vence, hoy, diasAlerta);
            if (estado is not ("POR_VENCER" or "VENCIDA"))
            {
                continue;
            }
            var vencida = estado == "VENCIDA";
            var dias = Math.Abs(vence.DayNumber - hoy.DayNumber);
            var licencia = $"{e.CategoriaLicencia} {e.NumeroLicencia}".Trim();
            resultado.Add(new($"licencia:{e.Id}:{vence:yyyy-MM-dd}:{estado}", CategoriaNotificacion.Vencimientos,
                vencida ? NivelNotificacion.Critica : NivelNotificacion.Advertencia, $"Licencia {(vencida ? "vencida" : "por vencer")} · {e.NombreCompleto}",
                vencida
                    ? $"La licencia de conducción de {e.NombreCompleto} ({licencia}) venció el {Fechas.Corta(vence)}, hace {Plural(dias, "día", "días")}. " +
                      "No puede tener rutas asignadas hasta renovarla."
                    : $"La licencia de conducción de {e.NombreCompleto} ({licencia}) vence el {Fechas.Corta(vence)}, en {Plural(dias, "día", "días")}.",
                "/operacion/conductores", "Ver conductores"));
        }

        // Envíos automáticos de la programación de cobro
        var envios = await db.EnviosProgramacion.AsNoTracking().Include(x => x.Programacion).ThenInclude(p => p.Factura)
            .Where(x => x.Fecha >= desde).ToListAsync(ct);
        foreach (var envio in envios)
        {
            var canal = envio.Canal == CanalEnvio.Whatsapp ? "WhatsApp" : "correo";
            var fallido = envio.Estado == EstadoEnvio.Fallido;
            var p = envio.Programacion;
            var mes = Periodos.Nombre(envio.Periodo, true);
            resultado.Add(new($"envio:{envio.Id}", CategoriaNotificacion.Envios, fallido ? NivelNotificacion.Critica : NivelNotificacion.Informativa,
                fallido ? $"Falló un envío por {canal}" : $"Recordatorio enviado por {canal}",
                fallido
                    ? $"No se pudo enviar el recordatorio de {mes} de \"{p.Nombre}\" (factura {p.Factura.NumeroFactura}) a {envio.Destinatario}: " +
                      $"{envio.Detalle} Revisa los datos de contacto del acudiente."
                    : $"Se envió el recordatorio de {mes} de \"{p.Nombre}\" (factura {p.Factura.NumeroFactura}) a {envio.Destinatario}.",
                "/finanzas/programacion-facturacion", "Ver programación", envio.Fecha));
        }

        return resultado;
    }

    public static string NombreNovedad(TipoNovedadVehiculo t) => t switch
    {
        TipoNovedadVehiculo.Averia => "Avería",
        TipoNovedadVehiculo.Dano => "Daño",
        TipoNovedadVehiculo.Llantas => "Llantas",
        TipoNovedadVehiculo.Mantenimiento => "Mantenimiento",
        TipoNovedadVehiculo.Limpieza => "Aseo",
        TipoNovedadVehiculo.Documentos => "Documentos",
        _ => "Otra",
    };

    /// <summary>"enero y febrero de 2026" (meses del recibo; los recibos antiguos sin detalle usan su periodo).</summary>
    private static string MesesRecibo(Domain.Finanzas.Pago r)
    {
        var meses = r.Detalle.Select(d => d.Periodo).Distinct().Order().Select(p => Periodos.Nombre(p, true)).ToList();
        if (meses.Count == 0) return Periodos.Nombre(r.Periodo ?? r.FechaPago, true);
        return meses.Count == 1 ? meses[0] : string.Join(", ", meses.Take(meses.Count - 1)) + " y " + meses[^1];
    }

    private static string Recortar(string texto, int maximo) => texto.Length <= maximo ? texto : texto[..(maximo - 1)].TrimEnd() + "…";

    private static string Plural(int n, string uno, string varios) => $"{n} {(n == 1 ? uno : varios)}";

    private static string NombreMedio(MetodoPago m) => m switch
    {
        MetodoPago.Efectivo => "Efectivo",
        MetodoPago.Transferencia => "Transferencia bancaria",
        MetodoPago.Nequi => "Nequi",
        MetodoPago.Daviplata => "Daviplata",
        _ => "Tarjeta débito/crédito",
    };

    private static string NombreTipo(TipoVehiculo t) => t switch
    {
        TipoVehiculo.Bus => "Bus",
        TipoVehiculo.Buseta => "Buseta",
        TipoVehiculo.Microbus => "Microbús",
        _ => "Camioneta",
    };

    /// <summary>Campo del frontend (parte de la clave) y nombre del documento.</summary>
    private static (string Campo, string Nombre, bool Femenino, bool Plural) Documento(TipoDocumentoVehiculo t) => t switch
    {
        TipoDocumentoVehiculo.Soat => ("vencimientoSoat", "SOAT", false, false),
        TipoDocumentoVehiculo.Tecnomecanica => ("vencimientoTecnomecanica", "Revisión técnico-mecánica", true, false),
        TipoDocumentoVehiculo.TarjetaOperacion => ("vencimientoTarjetaOperacion", "Tarjeta de operación", true, false),
        _ => ("vencimientoPolizas", "Pólizas", true, true),
    };
}

/// <summary>
/// Cuándo revisar de nuevo las notificaciones de una empresa (singleton). La campana pide el resumen cada
/// minuto y tras cada cambio; se genera si pasó <see cref="Intervalo"/> o si hubo una escritura desde la
/// última revisión (<see cref="Marcar"/>, desde el pipeline). Con varias instancias del API cada una lleva su
/// propio control; el índice único usuario + clave evita duplicados.
/// </summary>
public class RevisionNotificaciones(TimeProvider reloj)
{
    public static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<Guid, DateTimeOffset> ultima = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> candados = new();

    /// <summary>Hubo cambios en la empresa: la próxima consulta vuelve a generar.</summary>
    public void Marcar(Guid idEmpresa) => ultima.TryRemove(idEmpresa, out _);

    /// <summary>Ejecuta <paramref name="generar"/> si toca, una sola vez a la vez por empresa.</summary>
    public async Task RevisarAsync(Guid idEmpresa, Func<Task> generar, CancellationToken ct)
    {
        if (!Toca(idEmpresa))
        {
            return;
        }
        var candado = candados.GetOrAdd(idEmpresa, _ => new SemaphoreSlim(1, 1));
        await candado.WaitAsync(ct);
        try
        {
            if (!Toca(idEmpresa))
            {
                return;
            }
            // Antes de leer los datos: un cambio durante la generación vuelve a marcar la empresa.
            ultima[idEmpresa] = reloj.GetUtcNow();
            await generar();
        }
        finally
        {
            candado.Release();
        }
    }

    private bool Toca(Guid idEmpresa) => !ultima.TryGetValue(idEmpresa, out var fecha) || reloj.GetUtcNow() - fecha >= Intervalo;
}
