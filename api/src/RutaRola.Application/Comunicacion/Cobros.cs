using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Application.Finanzas;
using RutaRola.Domain.Comunicacion;
using RutaRola.Domain.Enums;
using RutaRola.Domain.Finanzas;

namespace RutaRola.Application.Comunicacion;

/// <summary>Resultado de entregar un mensaje al proveedor. <c>Detalle</c> se muestra en el historial de envíos.</summary>
public record ResultadoEnvio(bool Exito, string Detalle, string? IdExterno = null);

/// <summary>Adjunto de un correo (contenido completo).</summary>
public record AdjuntoCorreo(string Nombre, string Tipo, byte[] Contenido);

/// <summary>
/// Canal de WhatsApp (WhatsApp Business u otro proveedor). <see cref="Habilitado"/> = false: el proceso de
/// cobro no envía por este canal ni deja registro, así que los recordatorios salen cuando se configure.
/// </summary>
public interface ICanalWhatsapp
{
    bool Habilitado { get; }
    Task<ResultadoEnvio> EnviarAsync(string telefono, string mensaje, CancellationToken ct);
}

/// <summary>Canal de correo (SMTP o un servicio de correo). Mismo criterio que <see cref="ICanalWhatsapp"/>.</summary>
public interface ICanalCorreo
{
    bool Habilitado { get; }
    Task<ResultadoEnvio> EnviarAsync(string destinatario, string asunto, string cuerpo, IReadOnlyList<AdjuntoCorreo> adjuntos, CancellationToken ct);
}

/// <summary>
/// Proceso de cobro de la empresa del contexto (lo ejecuta el proceso del servidor): por cada programación
/// activa de una factura vigente, la mensualidad del mes en curso sin recibo se recuerda desde
/// <c>fecha límite − días previos</c> hasta la fecha límite, una vez por canal habilitado y mes (índice único
/// programación + periodo + canal; un fallido no se reintenta: lo avisa la campana). El texto es la
/// plantilla EN USO con las variables de <c>VARIABLES_MENSAJE</c>. Los mensajes de WhatsApp quedan en
/// <c>mensajes_whatsapp</c>.
/// </summary>
public class ServicioCobros(
    IBaseDatos db, ICanalWhatsapp whatsapp, ICanalCorreo correo, TimeProvider reloj, Suscripciones.ServicioSuscripcion suscripcion)
{
    public async Task<int> EnviarRecordatoriosAsync(CancellationToken ct = default)
    {
        if (!whatsapp.Habilitado && !correo.Habilitado)
        {
            return 0;
        }
        // Los recordatorios automáticos están incluidos desde el plan Esencial.
        if (!await suscripcion.TieneFuncionAsync(Suscripciones.FuncionPlan.Recordatorios, ct))
        {
            return 0;
        }
        var hoy = Fechas.Hoy(reloj);
        var periodo = Periodos.De(hoy);
        var programaciones = await db.ProgramacionesCobro
            .Include(p => p.Factura).ThenInclude(f => f.Acudiente)
            .Include(p => p.Plantillas).ThenInclude(x => x.Adjuntos).ThenInclude(a => a.Archivo)
            .Where(p => p.Activo && p.Factura.Estado == EstadoFactura.Vigente && p.Factura.PeriodoInicio <= periodo && periodo <= p.Factura.PeriodoFin)
            .ToListAsync(ct);
        var ids = programaciones.Select(p => p.IdFactura).ToList();
        var pagadas = (await db.Pagos.AsNoTracking()
            .Where(p => p.Estado == EstadoPago.Vigente && p.IdFactura != null && ids.Contains(p.IdFactura.Value) && p.Periodo == periodo)
            .Select(p => p.IdFactura!.Value).ToListAsync(ct)).ToHashSet();
        var idsProgramaciones = programaciones.Select(p => p.Id).ToList();
        var enviados = (await db.EnviosProgramacion.AsNoTracking()
            .Where(e => idsProgramaciones.Contains(e.IdProgramacion) && e.Periodo == periodo)
            .Select(e => new { e.IdProgramacion, e.Canal }).ToListAsync(ct))
            .Select(e => (e.IdProgramacion, e.Canal)).ToHashSet();

        var total = 0;
        foreach (var p in programaciones)
        {
            var limite = Cobro.FechaLimite(periodo, p.DiaLimitePago);
            if (hoy < limite.AddDays(-p.DiasPrevios) || hoy > limite || pagadas.Contains(p.IdFactura))
            {
                continue;
            }
            var variables = Variables(p.Factura, periodo, limite);
            if (p.Whatsapp && whatsapp.Habilitado && !enviados.Contains((p.Id, CanalEnvio.Whatsapp)) && EnUso(p, CanalEnvio.Whatsapp) is { } w)
            {
                await EnviarWhatsappAsync(p, periodo, Aplicar(w.Cuerpo, variables), ct);
                total++;
            }
            if (p.Email && correo.Habilitado && !enviados.Contains((p.Id, CanalEnvio.Email)) && EnUso(p, CanalEnvio.Email) is { } c)
            {
                await EnviarCorreoAsync(p, periodo, Aplicar(c.Asunto ?? "", variables), Aplicar(c.Cuerpo, variables), c, ct);
                total++;
            }
        }
        return total;
    }

    private static PlantillaMensaje? EnUso(ProgramacionCobro p, CanalEnvio canal) =>
        p.Plantillas.FirstOrDefault(x => x.Canal == canal && x.Estado == EstadoPlantilla.EnUso);

    /// <summary>Variables de <c>VARIABLES_MENSAJE</c> (<c>programacion.ts</c>).</summary>
    public static IReadOnlyDictionary<string, string> Variables(Factura f, DateOnly periodo, DateOnly limite) => new Dictionary<string, string>
    {
        ["{acudiente}"] = f.NombreAcudiente,
        ["{estudiante}"] = f.NombreEstudiante,
        ["{factura}"] = f.NumeroFactura,
        ["{mes}"] = Periodos.Nombre(periodo, true),
        ["{valor}"] = Cobro.Pesos(f.ValorMensual),
        ["{fecha_limite}"] = Fechas.Corta(limite),
    };

    public static string Aplicar(string texto, IReadOnlyDictionary<string, string> variables) =>
        variables.Aggregate(texto, (t, v) => t.Replace(v.Key, v.Value, StringComparison.Ordinal));

    private async Task EnviarWhatsappAsync(ProgramacionCobro p, DateOnly periodo, string mensaje, CancellationToken ct)
    {
        var telefono = p.Factura.Acudiente.Telefono.Trim();
        var resultado = telefono.Length == 0
            ? new ResultadoEnvio(false, "El acudiente no tiene teléfono registrado.")
            : await Intentar(() => whatsapp.EnviarAsync(telefono, mensaje, ct));
        var ahora = reloj.GetUtcNow().UtcDateTime;
        var registro = new MensajeWhatsapp
        {
            IdAcudiente = p.Factura.IdAcudiente, IdEstudiante = p.Factura.IdEstudiante, Telefono = telefono, Mensaje = mensaje,
            Estado = resultado.Exito ? EstadoMensaje.Enviado : EstadoMensaje.Fallido, FechaCreacion = ahora,
            FechaEnvio = resultado.Exito ? ahora : null, Respuesta = resultado.Detalle, IdExterno = resultado.IdExterno,
        };
        db.MensajesWhatsapp.Add(registro);
        await GuardarEnvioAsync(p, periodo, CanalEnvio.Whatsapp, telefono, resultado, registro.Id, ct);
    }

    private async Task EnviarCorreoAsync(ProgramacionCobro p, DateOnly periodo, string asunto, string cuerpo, PlantillaMensaje plantilla, CancellationToken ct)
    {
        var destino = (p.Factura.Acudiente.Correo ?? "").Trim();
        var adjuntos = plantilla.Adjuntos.Where(a => a.Archivo.Contenido is not null)
            .Select(a => new AdjuntoCorreo(a.Archivo.Nombre, a.Archivo.TipoArchivo, a.Archivo.Contenido!)).ToList();
        var resultado = destino.Length == 0
            ? new ResultadoEnvio(false, "El acudiente no tiene correo registrado.")
            : await Intentar(() => correo.EnviarAsync(destino, asunto, cuerpo, adjuntos, ct));
        await GuardarEnvioAsync(p, periodo, CanalEnvio.Email, destino, resultado, null, ct);
    }

    /// <summary>Un error del proveedor queda como envío FALLIDO, no detiene el proceso.</summary>
    private static async Task<ResultadoEnvio> Intentar(Func<Task<ResultadoEnvio>> enviar)
    {
        try
        {
            return await enviar();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ResultadoEnvio(false, "El proveedor de mensajes no respondió. Se intentará con el próximo cobro.");
        }
    }

    private async Task GuardarEnvioAsync(
        ProgramacionCobro p, DateOnly periodo, CanalEnvio canal, string destinatario, ResultadoEnvio resultado, Guid? idMensaje, CancellationToken ct)
    {
        p.Envios.Add(new EnvioProgramacion
        {
            IdProgramacion = p.Id, Fecha = reloj.GetUtcNow().UtcDateTime, Canal = canal, Destinatario = destinatario, Periodo = periodo,
            Estado = resultado.Exito ? EstadoEnvio.Enviado : EstadoEnvio.Fallido, Detalle = resultado.Detalle, IdMensajeWhatsapp = idMensaje,
        });
        // Uno por uno: si el proveedor ya entregó el mensaje, el registro no se pierde por un error en otro.
        await db.SaveChangesAsync(ct);
    }
}
