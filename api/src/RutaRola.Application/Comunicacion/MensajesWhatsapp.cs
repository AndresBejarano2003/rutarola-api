using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Domain.Enums;

namespace RutaRola.Application.Comunicacion;

public record MensajeWhatsappDto(
    Guid Id, Guid AcudienteId, string NombreAcudiente, Guid? EstudianteId, string NombreEstudiante, string Telefono, string Mensaje,
    EstadoMensaje Estado, DateTime FechaCreacion, DateTime? FechaEnvio, string Respuesta);

/// <summary><c>desde</c>/<c>hasta</c>: fecha (Colombia) de creación.</summary>
public record FiltrosMensajeWhatsapp(
    string? Texto = null, Guid? AcudienteId = null, string? Estado = null, DateOnly? Desde = null, DateOnly? Hasta = null);

/// <summary>Registro de mensajes de WhatsApp enviados por el servidor (<c>mensajes_whatsapp</c>), del más reciente al más antiguo.</summary>
public class ServicioMensajesWhatsapp(IBaseDatos db)
{
    public async Task<ListaPaginada<MensajeWhatsappDto>> ListarAsync(FiltrosMensajeWhatsapp f, Paginacion paginacion, CancellationToken ct = default)
    {
        var consulta = db.MensajesWhatsapp.AsNoTracking();
        if (f.AcudienteId is { } acudiente)
        {
            consulta = consulta.Where(m => m.IdAcudiente == acudiente);
        }
        if (TextoEnum.Leer<EstadoMensaje>(f.Estado) is { } estado)
        {
            consulta = consulta.Where(m => m.Estado == estado);
        }
        if (f.Desde is { } desde)
        {
            var inicio = DateTime.SpecifyKind(desde.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc) - Fechas.DesfaseColombia;
            consulta = consulta.Where(m => m.FechaCreacion >= inicio);
        }
        if (f.Hasta is { } hasta)
        {
            var fin = DateTime.SpecifyKind(hasta.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc) - Fechas.DesfaseColombia;
            consulta = consulta.Where(m => m.FechaCreacion < fin);
        }
        if (Texto.Opcional(f.Texto) is { } texto)
        {
            var busqueda = Busqueda.Normalizar(texto);
            consulta = consulta.Where(m => FuncionesBd.SinTildes(m.Telefono + " " + m.Mensaje + " " + m.Acudiente.PrimerNombre + " " + m.Acudiente.PrimerApellido)
                .Contains(busqueda));
        }
        var total = await consulta.CountAsync(ct);
        var items = await consulta.OrderByDescending(m => m.FechaCreacion).ThenByDescending(m => m.Id)
            .Skip(paginacion.Omitir).Take(paginacion.TamanoValido)
            .Select(m => new
            {
                Mensaje = m,
                m.Acudiente.PrimerNombre, m.Acudiente.SegundoNombre, m.Acudiente.PrimerApellido, m.Acudiente.SegundoApellido,
                Estudiante = m.Estudiante == null ? null : new { m.Estudiante.PrimerNombre, m.Estudiante.SegundoNombre, m.Estudiante.PrimerApellido, m.Estudiante.SegundoApellido },
            })
            .ToListAsync(ct);
        static string Nombre(string a, string? b, string c, string? d) => string.Join(' ', new[] { a, b, c, d }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return new(items.Select(x => new MensajeWhatsappDto(
            x.Mensaje.Id, x.Mensaje.IdAcudiente, Nombre(x.PrimerNombre, x.SegundoNombre, x.PrimerApellido, x.SegundoApellido), x.Mensaje.IdEstudiante,
            x.Estudiante is null ? "" : Nombre(x.Estudiante.PrimerNombre, x.Estudiante.SegundoNombre, x.Estudiante.PrimerApellido, x.Estudiante.SegundoApellido),
            x.Mensaje.Telefono, x.Mensaje.Mensaje, x.Mensaje.Estado, x.Mensaje.FechaCreacion, x.Mensaje.FechaEnvio, x.Mensaje.Respuesta ?? "")).ToList(),
            total, paginacion.PaginaValida, paginacion.TamanoValido);
    }
}
