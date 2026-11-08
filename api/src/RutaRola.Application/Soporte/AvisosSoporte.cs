using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RutaRola.Application.Comunicacion;
using RutaRola.Application.Comun;
using RutaRola.Domain.Comunicacion;
using RutaRola.Domain.Enums;
using RutaRola.Domain.Seguridad;
using RutaRola.Domain.Soporte;

namespace RutaRola.Application.Soporte;

/// <summary>Sección <c>Soporte</c>.</summary>
public class OpcionesSoporte
{
    public const string Seccion = "Soporte";

    /// <summary>Buzón del equipo de soporte. Vacío = el correo de cada SuperAdmin activo.</summary>
    public string CorreoEquipo { get; set; } = "";
    /// <summary>URL pública del frontend para los enlaces de los correos (sin <c>/#/</c>).</summary>
    public string UrlFrontend { get; set; } = "https://rutarola.netlify.app";
}

/// <summary>
/// Avisos de un ticket: correo al equipo de soporte cuando escribe la empresa, y correo + campana al
/// solicitante cuando responde la plataforma (nunca por notas internas). Un fallo del correo nunca hace
/// fallar la operación; no se registran destinatarios ni textos.
/// </summary>
public class AvisosSoporte(
    IBaseDatos db, ICanalCorreo correo, IOptions<OpcionesSoporte> opciones, TimeProvider reloj, ILogger<AvisosSoporte> logger)
{
    private OpcionesSoporte Opciones => opciones.Value;

    public string EnlaceEmpresa(Guid idTicket) => $"{Opciones.UrlFrontend.TrimEnd('/')}/#/sistema/soporte?ticket={idTicket}";

    public string EnlacePlataforma(Guid idTicket) => $"{Opciones.UrlFrontend.TrimEnd('/')}/#/plataforma/soporte?ticket={idTicket}";

    /// <summary>La empresa creó el ticket: aviso al equipo y confirmación al solicitante.</summary>
    public async Task CreadoAsync(TicketSoporte t, string empresa, IReadOnlyList<AdjuntoCorreo> adjuntos, CancellationToken ct)
    {
        await AlEquipoAsync(t, $"[{t.Numero}] Nuevo ticket de {empresa}: {t.Asunto}", string.Join('\n',
            $"{empresa} abrió un ticket de soporte.",
            "",
            $"Número: {t.Numero}",
            $"Tipo: {ReglasTicket.Nombre(t.Tipo)} · Prioridad: {ReglasTicket.Nombre(t.Prioridad)} · Módulo: {t.Modulo}",
            $"Solicitante: {t.NombreSolicitante}{(t.CorreoSolicitante is { } c ? $" <{c}>" : "")}",
            $"Asunto: {t.Asunto}",
            "",
            t.Descripcion,
            "",
            $"Atenderlo: {EnlacePlataforma(t.Id)}"), adjuntos, ct);
        await AlSolicitanteAsync(t, $"[{t.Numero}] Recibimos tu solicitud: {t.Asunto}", string.Join('\n',
            $"Hola, {t.NombreSolicitante}.",
            "",
            $"Recibimos tu solicitud {t.Numero} ({ReglasTicket.Nombre(t.Tipo).ToLowerInvariant()}, prioridad {ReglasTicket.Nombre(t.Prioridad).ToLowerInvariant()}).",
            $"Nuestro objetivo es resolverla antes del {t.FechaObjetivo:yyyy-MM-dd HH:mm} (UTC). Te avisaremos por este medio y en la campana de RutaRola en cada avance.",
            "",
            $"Sigue su avance: {EnlaceEmpresa(t.Id)}"), conCampana: false, ct);
    }

    /// <summary>La empresa respondió, validó, reabrió o canceló: aviso al equipo.</summary>
    public Task DeLaEmpresaAsync(TicketSoporte t, string empresa, string accion, string? texto, CancellationToken ct) =>
        AlEquipoAsync(t, $"[{t.Numero}] {accion} · {empresa}", string.Join('\n',
            $"{t.NombreSolicitante} ({empresa}) — {accion.ToLowerInvariant()} en {t.Numero}: {t.Asunto}",
            $"Estado actual: {ReglasTicket.Nombre(t.Estado)}",
            "",
            texto ?? "",
            "",
            $"Ver el ticket: {EnlacePlataforma(t.Id)}"), [], ct);

    /// <summary>La plataforma avanzó el ticket o respondió: correo y campana al solicitante.</summary>
    public Task DeLaPlataformaAsync(TicketSoporte t, string titulo, string mensaje, CancellationToken ct) =>
        AlSolicitanteAsync(t, $"[{t.Numero}] {titulo}", string.Join('\n',
            $"Hola, {t.NombreSolicitante}.",
            "",
            $"{titulo} — {t.Numero}: {t.Asunto}",
            $"Estado: {ReglasTicket.Nombre(t.Estado)}",
            "",
            mensaje,
            "",
            $"Ver el ticket: {EnlaceEmpresa(t.Id)}"), conCampana: true, ct, titulo, mensaje);

    private async Task AlEquipoAsync(TicketSoporte t, string asunto, string cuerpo, IReadOnlyList<AdjuntoCorreo> adjuntos, CancellationToken ct)
    {
        var destinos = Texto.Opcional(Opciones.CorreoEquipo) is { } buzon
            ? [buzon]
            : await db.Usuarios.IgnoreQueryFilters()
                .Where(u => u.Activo && u.Rol.Nombre == Roles.SuperAdmin && u.Email != null && u.Email != "")
                .Select(u => u.Email!).ToListAsync(ct);
        foreach (var destino in destinos)
        {
            await EnviarAsync(destino, asunto, cuerpo, adjuntos, ct);
        }
    }

    private async Task AlSolicitanteAsync(
        TicketSoporte t, string asunto, string cuerpo, bool conCampana, CancellationToken ct, string? titulo = null, string? mensaje = null)
    {
        if (conCampana)
        {
            db.Notificaciones.Add(new Notificacion
            {
                IdUsuario = t.IdSolicitante,
                Clave = $"soporte:{t.Id}:{Guid.CreateVersion7()}",
                Categoria = CategoriaNotificacion.Sistema,
                Nivel = t.Estado is EstadoTicket.Resuelto or EstadoTicket.Cerrado ? NivelNotificacion.Exito
                    : t.Estado is EstadoTicket.EsperandoCliente or EstadoTicket.ValidacionCliente ? NivelNotificacion.Advertencia
                    : NivelNotificacion.Informativa,
                Titulo = $"{t.Numero}: {titulo}",
                Mensaje = Texto.Recortar(mensaje ?? t.Asunto, 500),
                FechaCreacion = reloj.GetUtcNow().UtcDateTime,
                Enlace = $"/sistema/soporte?ticket={t.Id}",
                TextoEnlace = "Ver el ticket",
            });
            await db.SaveChangesAsync(ct);
        }
        if (Texto.Opcional(t.CorreoSolicitante) is { } destino)
        {
            await EnviarAsync(destino, asunto, cuerpo, [], ct);
        }
    }

    private async Task EnviarAsync(string destino, string asunto, string cuerpo, IReadOnlyList<AdjuntoCorreo> adjuntos, CancellationToken ct)
    {
        if (!correo.Habilitado) return;
        try
        {
            var resultado = await correo.EnviarAsync(destino, asunto, cuerpo, adjuntos, ct);
            if (!resultado.Exito)
            {
                logger.LogWarning("No se pudo enviar un correo de soporte: {Detalle}", resultado.Detalle);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Falló el envío de un correo de soporte");
        }
    }
}
