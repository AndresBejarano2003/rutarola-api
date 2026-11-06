using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RutaRola.Domain.Comunicacion;

namespace RutaRola.Application.Comun;

/// <summary>
/// Archivo adjunto como lo maneja <c>&lt;app-archivo-adjunto&gt;</c> del frontend. Al guardar, <c>url</c>
/// es un data URL (base64) si el archivo es nuevo, o la URL que el API devolvió si no cambió;
/// <c>null</c> quita el adjunto.
/// </summary>
public record ArchivoAdjuntoDto(string? Nombre, string? Tipo, long Tamano, string? Url);

public static class Adjuntos
{
    /// <summary>Igual que el formulario (2 MB).</summary>
    public const int TamanoMaximo = 2 * 1024 * 1024;

    /// <summary>PDF o imagen.</summary>
    public static bool TipoPermitido(string? tipo) =>
        tipo is not null && (tipo == "application/pdf" || tipo.StartsWith("image/", StringComparison.Ordinal));

    public static bool EsDataUrl(string? url) => url?.StartsWith("data:", StringComparison.Ordinal) == true;

    /// <summary>En los listados solo van los metadatos (<c>url</c> vacía); el detalle trae el contenido.</summary>
    public static ArchivoAdjuntoDto Dto(Archivo archivo, bool conContenido) => new(
        archivo.Nombre, archivo.TipoArchivo, archivo.Tamano,
        conContenido && archivo.Contenido is { } contenido
            ? $"data:{archivo.TipoArchivo};base64,{Convert.ToBase64String(contenido)}"
            : archivo.UrlArchivo ?? "");

    public static void ReglasAdjunto<T>(this IRuleBuilder<T, ArchivoAdjuntoDto> regla, string campo) =>
        regla.ChildRules(a =>
        {
            a.RuleFor(x => x.Nombre).NotEmpty().WithMessage($"Falta el nombre del {campo}.").MaximumLength(255);
            a.RuleFor(x => x.Tipo).Must(TipoPermitido).When(x => EsDataUrl(x.Url))
                .WithMessage($"El {campo} debe ser un PDF o una imagen.");
        });

    /// <summary>
    /// Aplica el adjunto de la solicitud: data URL → reemplaza el actual; null → lo quita; otro valor →
    /// se conserva. Devuelve el id del archivo que queda (o null).
    /// </summary>
    public static async Task<Guid?> AplicarAsync(
        IBaseDatos db, Guid? idActual, ArchivoAdjuntoDto? solicitud, Guid? subidoPor, string campo, CancellationToken ct)
    {
        if (solicitud is not null && !EsDataUrl(solicitud.Url))
        {
            return idActual;
        }

        if (idActual is { } id && await db.Archivos.FirstOrDefaultAsync(a => a.Id == id, ct) is { } anterior)
        {
            db.Archivos.Remove(anterior);
        }
        if (solicitud is null)
        {
            return null;
        }

        var (tipo, contenido) = Leer(solicitud.Url!, campo);
        if (!TipoPermitido(tipo))
        {
            throw new ErrorNegocio($"El {campo} debe ser un PDF o una imagen.", CodigoError.Validacion);
        }
        var archivo = new Archivo
        {
            Nombre = solicitud.Nombre!.Trim(),
            TipoArchivo = tipo,
            Tamano = contenido.Length,
            Contenido = contenido,
            SubidoPor = subidoPor,
        };
        db.Archivos.Add(archivo);
        return archivo.Id;
    }

    public static async Task EliminarAsync(IBaseDatos db, Guid? id, CancellationToken ct)
    {
        if (id is { } valor && await db.Archivos.FirstOrDefaultAsync(a => a.Id == valor, ct) is { } archivo)
        {
            db.Archivos.Remove(archivo);
        }
    }

    private static (string Tipo, byte[] Contenido) Leer(string dataUrl, string campo)
    {
        var coma = dataUrl.IndexOf(',');
        var cabecera = coma > 5 ? dataUrl[5..coma] : "";
        if (!cabecera.EndsWith(";base64", StringComparison.Ordinal))
        {
            throw new ErrorNegocio($"No se pudo leer el {campo}.", CodigoError.Validacion);
        }
        // Tamaño aproximado antes de decodificar, para no reservar memoria de más.
        if ((dataUrl.Length - coma) / 4L * 3 > TamanoMaximo + 3)
        {
            throw new ErrorNegocio($"El {campo} supera el máximo de {TamanoMaximo / 1024 / 1024} MB.", CodigoError.Validacion);
        }
        byte[] contenido;
        try
        {
            contenido = Convert.FromBase64String(dataUrl[(coma + 1)..]);
        }
        catch (FormatException)
        {
            throw new ErrorNegocio($"No se pudo leer el {campo}.", CodigoError.Validacion);
        }
        if (contenido.Length == 0)
        {
            throw new ErrorNegocio($"No se pudo leer el {campo}.", CodigoError.Validacion);
        }
        if (contenido.Length > TamanoMaximo)
        {
            throw new ErrorNegocio($"El {campo} supera el máximo de {TamanoMaximo / 1024 / 1024} MB.", CodigoError.Validacion);
        }
        return (cabecera[..^7].Split(';')[0].Trim().ToLowerInvariant(), contenido);
    }
}
