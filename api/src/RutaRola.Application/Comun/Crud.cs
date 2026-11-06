using Microsoft.EntityFrameworkCore;
using RutaRola.Domain.Comun;

namespace RutaRola.Application.Comun;

/// <summary>Opción de una lista de selección (<c>GET /x/opciones</c> → <c>[{ id, nombre }]</c>).</summary>
public record Opcion(Guid Id, string Nombre);

/// <summary><c>GET /x/{id}/motivo-no-eliminar</c>: null si se puede eliminar.</summary>
public record RespuestaMotivo(string? Motivo);

/// <summary>Operaciones que expone cada CRUD; las usa <c>ControladorCrud</c> del API.</summary>
public interface IServicioCrud<TDto, in TSolicitud, in TFiltros>
{
    Task<ListaPaginada<TDto>> ListarAsync(TFiltros filtros, Paginacion paginacion, CancellationToken ct = default);
    Task<IReadOnlyList<Opcion>> OpcionesAsync(TFiltros filtros, CancellationToken ct = default);
    Task<TDto> ObtenerAsync(Guid id, CancellationToken ct = default);
    Task<TDto> CrearAsync(TSolicitud solicitud, CancellationToken ct = default);
    Task<TDto> ActualizarAsync(Guid id, TSolicitud solicitud, CancellationToken ct = default);
    Task<string?> MotivoNoEliminarAsync(Guid id, CancellationToken ct = default);
    Task EliminarAsync(Guid id, CancellationToken ct = default);
}

/// <summary>
/// Flujo común de los CRUD (el mismo de <c>RepositorioLocal</c> del frontend): filtrar, ordenar y
/// paginar en la base de datos; mapear a DTO; validar y aplicar la solicitud; eliminar solo si
/// <see cref="MotivoNoEliminarAsync"/> es null (si no, 409 con el motivo). Todo pasa por el filtro
/// de empresa del contexto, así que un id de otra empresa responde 404.
/// </summary>
public abstract class ServicioCrud<TEntidad, TDto, TSolicitud, TFiltros>(IBaseDatos db)
    : IServicioCrud<TDto, TSolicitud, TFiltros>
    where TEntidad : Entidad, new()
{
    protected IBaseDatos Db { get; } = db;

    /// <summary>"El colegio", "El vehículo"… para el 404.</summary>
    protected abstract string Entidad { get; }

    protected abstract DbSet<TEntidad> Conjunto { get; }

    protected abstract IQueryable<TEntidad> Filtrar(IQueryable<TEntidad> consulta, TFiltros filtros);

    protected abstract IOrderedQueryable<TEntidad> Ordenar(IQueryable<TEntidad> consulta);

    /// <summary>Hijos que necesita el mapeo (cursos, documentos…). También se usa al actualizar.</summary>
    protected virtual IQueryable<TEntidad> Incluir(IQueryable<TEntidad> consulta) => consulta;

    /// <summary>Convierte a DTO. <paramref name="detalle"/> = consulta de un registro (incluye adjuntos).</summary>
    protected abstract Task<IReadOnlyList<TDto>> MapearAsync(IReadOnlyList<TEntidad> registros, bool detalle, CancellationToken ct);

    protected abstract string Etiqueta(TEntidad registro);

    /// <summary>Valida reglas de negocio (409/422/404 de referencias) y copia la solicitud a la entidad.</summary>
    protected abstract Task AplicarAsync(TEntidad registro, TSolicitud solicitud, bool nuevo, CancellationToken ct);

    public abstract Task<string?> MotivoNoEliminarAsync(Guid id, CancellationToken ct = default);

    /// <summary>Limpieza antes de borrar (usuario de acceso, adjuntos…).</summary>
    protected virtual Task AntesDeEliminarAsync(TEntidad registro, CancellationToken ct) => Task.CompletedTask;

    public async Task<ListaPaginada<TDto>> ListarAsync(TFiltros filtros, Paginacion paginacion, CancellationToken ct = default)
    {
        var consulta = Filtrar(Conjunto.AsNoTracking(), filtros);
        var total = await consulta.CountAsync(ct);
        var registros = await Incluir(Ordenar(consulta).Skip(paginacion.Omitir).Take(paginacion.TamanoValido))
            .ToListAsync(ct);
        var items = await MapearAsync(registros, false, ct);
        return new ListaPaginada<TDto>(items, total, paginacion.PaginaValida, paginacion.TamanoValido);
    }

    public virtual async Task<IReadOnlyList<Opcion>> OpcionesAsync(TFiltros filtros, CancellationToken ct = default)
    {
        var registros = await Ordenar(Filtrar(Conjunto.AsNoTracking(), filtros)).ToListAsync(ct);
        return registros.Select(r => new Opcion(r.Id, Etiqueta(r))).ToList();
    }

    public async Task<TDto> ObtenerAsync(Guid id, CancellationToken ct = default)
    {
        var registro = await Incluir(Conjunto.AsNoTracking()).FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw ErrorNegocio.NoEncontrado(Entidad);
        return (await MapearAsync([registro], true, ct))[0];
    }

    public async Task<TDto> CrearAsync(TSolicitud solicitud, CancellationToken ct = default)
    {
        var registro = new TEntidad();
        await AplicarAsync(registro, solicitud, true, ct);
        Conjunto.Add(registro);
        await Db.SaveChangesAsync(ct);
        return await ObtenerAsync(registro.Id, ct);
    }

    public async Task<TDto> ActualizarAsync(Guid id, TSolicitud solicitud, CancellationToken ct = default)
    {
        var registro = await BuscarAsync(id, ct);
        await AplicarAsync(registro, solicitud, false, ct);
        await Db.SaveChangesAsync(ct);
        return await ObtenerAsync(id, ct);
    }

    public async Task EliminarAsync(Guid id, CancellationToken ct = default)
    {
        var registro = await BuscarAsync(id, ct);
        if (await MotivoNoEliminarAsync(id, ct) is { } motivo)
        {
            throw new ErrorNegocio(motivo, CodigoError.EnUso);
        }
        await AntesDeEliminarAsync(registro, ct);
        Conjunto.Remove(registro);
        await Db.SaveChangesAsync(ct);
    }

    /// <summary>Registro con seguimiento (para modificarlo) o 404.</summary>
    protected async Task<TEntidad> BuscarAsync(Guid id, CancellationToken ct) =>
        await Incluir(Conjunto).FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw ErrorNegocio.NoEncontrado(Entidad);
}
