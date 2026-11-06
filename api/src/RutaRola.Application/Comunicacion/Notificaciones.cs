using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Domain.Comunicacion;
using RutaRola.Domain.Enums;

namespace RutaRola.Application.Comunicacion;

/// <summary><c>NotificacionSistema</c> del frontend; <c>enlace</c> es <c>null</c> si no lleva a ninguna pantalla.</summary>
public record NotificacionDto(
    Guid Id, string Clave, CategoriaNotificacion Categoria, NivelNotificacion Nivel, string Titulo, string Mensaje, DateTime Fecha, bool Leida,
    bool Archivada, string? Enlace, string TextoEnlace);

/// <summary><c>bandeja</c> ENTRADA (por defecto) | ARCHIVADAS; <c>lectura</c> NO_LEIDAS | LEIDAS.</summary>
public record FiltrosNotificacion(string? Bandeja = null, string? Texto = null, string? Categoria = null, string? Nivel = null, string? Lectura = null);

public record ResumenNotificacionesDto(int NoLeidas, int Criticas, IReadOnlyList<NotificacionDto> Recientes);

public record SolicitudLectura(IReadOnlyList<Guid>? Ids, bool Leida);

public record SolicitudArchivo(IReadOnlyList<Guid>? Ids, bool Archivada);

public record SolicitudIds(IReadOnlyList<Guid>? Ids);

public class ValidadorSolicitudLectura : AbstractValidator<SolicitudLectura>
{
    public ValidadorSolicitudLectura() => RuleFor(x => x.Ids).IdsValidos();
}

public class ValidadorSolicitudArchivo : AbstractValidator<SolicitudArchivo>
{
    public ValidadorSolicitudArchivo() => RuleFor(x => x.Ids).IdsValidos();
}

public class ValidadorSolicitudIds : AbstractValidator<SolicitudIds>
{
    public ValidadorSolicitudIds() => RuleFor(x => x.Ids).IdsValidos();
}

internal static class ReglasNotificaciones
{
    public const int MaximoIds = 1000;

    public static IRuleBuilderOptions<T, IReadOnlyList<Guid>?> IdsValidos<T>(this IRuleBuilder<T, IReadOnlyList<Guid>?> regla) =>
        regla.NotNull().WithMessage("Indica las notificaciones.")
            .Must(ids => ids is null || ids.Count <= MaximoIds).WithMessage($"Máximo {MaximoIds} notificaciones por operación.");
}

/// <summary>
/// Bandeja del usuario del token (<c>inicio/notificaciones/notificaciones-http.ts</c>). Solo afecta sus
/// notificaciones: ids ajenos o inexistentes se ignoran. Eliminar es lógico (<c>eliminada</c>) para que el
/// mismo hecho no se vuelva a notificar. El resumen de la campana dispara la revisión de la empresa.
/// </summary>
public class ServicioNotificaciones(
    IBaseDatos db, IContextoUsuario usuario, GeneradorNotificaciones generador, RevisionNotificaciones revision)
{
    public const int RecientesCampana = 6;

    private IQueryable<Notificacion> Propias()
    {
        var id = usuario.IdUsuario ?? Guid.Empty;
        return db.Notificaciones.Where(n => n.IdUsuario == id && !n.Eliminada);
    }

    private static bool Archivadas(string? bandeja) => string.Equals(bandeja, "ARCHIVADAS", StringComparison.OrdinalIgnoreCase);

    public async Task<ListaPaginada<NotificacionDto>> ListarAsync(FiltrosNotificacion f, Paginacion paginacion, CancellationToken ct = default)
    {
        var archivadas = Archivadas(f.Bandeja);
        var consulta = Propias().AsNoTracking().Where(n => n.Archivada == archivadas);
        if (TextoEnum.Leer<CategoriaNotificacion>(f.Categoria) is { } categoria)
        {
            consulta = consulta.Where(n => n.Categoria == categoria);
        }
        if (TextoEnum.Leer<NivelNotificacion>(f.Nivel) is { } nivel)
        {
            consulta = consulta.Where(n => n.Nivel == nivel);
        }
        if (Texto.Opcional(f.Lectura) is { } lectura)
        {
            var leidas = lectura.Equals("LEIDAS", StringComparison.OrdinalIgnoreCase);
            consulta = consulta.Where(n => n.Leida == leidas);
        }
        if (Texto.Opcional(f.Texto) is { } texto)
        {
            var busqueda = Busqueda.Normalizar(texto);
            consulta = consulta.Where(n => FuncionesBd.SinTildes(n.Titulo + " " + n.Mensaje).Contains(busqueda));
        }
        var total = await consulta.CountAsync(ct);
        var items = await consulta.OrderByDescending(n => n.FechaCreacion).ThenByDescending(n => n.Id)
            .Skip(paginacion.Omitir).Take(paginacion.TamanoValido).ToListAsync(ct);
        return new(items.Select(Mapear).ToList(), total, paginacion.PaginaValida, paginacion.TamanoValido);
    }

    public async Task<ResumenNotificacionesDto> ResumenAsync(CancellationToken ct = default)
    {
        if (usuario.IdEmpresa is { } empresa)
        {
            await revision.RevisarAsync(empresa, () => generador.GenerarAsync(ct), ct);
        }
        var entrada = Propias().AsNoTracking().Where(n => !n.Archivada);
        var noLeidas = await entrada.CountAsync(n => !n.Leida, ct);
        var criticas = await entrada.CountAsync(n => !n.Leida && n.Nivel == NivelNotificacion.Critica, ct);
        var recientes = await entrada.OrderByDescending(n => n.FechaCreacion).ThenByDescending(n => n.Id).Take(RecientesCampana).ToListAsync(ct);
        return new(noLeidas, criticas, recientes.Select(Mapear).ToList());
    }

    public Task MarcarLeidasAsync(SolicitudLectura s, CancellationToken ct = default) =>
        Propias().Where(n => s.Ids!.Contains(n.Id)).ExecuteUpdateAsync(u => u.SetProperty(n => n.Leida, s.Leida), ct);

    public Task MarcarTodasLeidasAsync(CancellationToken ct = default) =>
        Propias().Where(n => !n.Archivada && !n.Leida).ExecuteUpdateAsync(u => u.SetProperty(n => n.Leida, true), ct);

    /// <summary>Archivar también marca como leída; desarchivar la devuelve a la entrada sin cambiar la lectura.</summary>
    public Task ArchivarAsync(SolicitudArchivo s, CancellationToken ct = default)
    {
        var consulta = Propias().Where(n => s.Ids!.Contains(n.Id));
        return s.Archivada
            ? consulta.ExecuteUpdateAsync(u => u.SetProperty(n => n.Archivada, true).SetProperty(n => n.Leida, true), ct)
            : consulta.ExecuteUpdateAsync(u => u.SetProperty(n => n.Archivada, false), ct);
    }

    public Task EliminarAsync(SolicitudIds s, CancellationToken ct = default) =>
        Propias().Where(n => s.Ids!.Contains(n.Id)).ExecuteUpdateAsync(u => u.SetProperty(n => n.Eliminada, true), ct);

    public Task VaciarAsync(string? bandeja, CancellationToken ct = default)
    {
        var archivadas = Archivadas(bandeja);
        return Propias().Where(n => n.Archivada == archivadas).ExecuteUpdateAsync(u => u.SetProperty(n => n.Eliminada, true), ct);
    }

    public static NotificacionDto Mapear(Notificacion n) => new(
        n.Id, n.Clave, n.Categoria, n.Nivel, n.Titulo, n.Mensaje, n.FechaCreacion, n.Leida, n.Archivada,
        string.IsNullOrEmpty(n.Enlace) ? null : n.Enlace, n.TextoEnlace ?? "");
}
