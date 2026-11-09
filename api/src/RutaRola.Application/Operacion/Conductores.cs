using RutaRola.Application.Configuracion;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Application.Personas;
using RutaRola.Domain.Enums;

namespace RutaRola.Application.Operacion;

public record AsignacionConductorDto(Guid Id, string Ruta, string Jornada, string Vehiculo, string Monitora);

/// <summary>Vista operativa de un conductor (<c>ConductorOperacion</c>): licencia y asignación activa.</summary>
public record ConductorDto(
    Guid Id, string Nombre, string Iniciales, string Documento, string Telefono, string Correo, string NumeroLicencia,
    string CategoriaLicencia, DateOnly? VencimientoLicencia, string? EstadoLicencia, string Estado,
    AsignacionConductorDto? Asignacion, string Foto);

/// <summary>
/// <c>licencia</c>: VIGENTE | POR_VENCER | VENCIDA; <c>asignacion</c>: CON (con ruta asignada) | SIN.
/// </summary>
public record FiltrosConductor(string? Texto = null, string? Licencia = null, string? Asignacion = null, string? Estado = null);

/// <summary>Solo lectura: los datos viven en empleados; se agregan la licencia y la asignación activa.</summary>
public class ServicioConductores(IBaseDatos db, TimeProvider reloj, ParametrosEmpresa parametros)
{
    public async Task<ListaPaginada<ConductorDto>> ListarAsync(FiltrosConductor f, Paginacion paginacion, CancellationToken ct = default)
    {
        var consulta = db.Empleados.AsNoTracking().Where(e => e.TipoEmpleado == TipoEmpleado.Conductor);
        if (Texto.Opcional(f.Texto) is { } texto)
        {
            var busqueda = Busqueda.Normalizar(texto);
            consulta = consulta.Where(e => FuncionesBd.SinTildes(
                e.PrimerNombre + (e.SegundoNombre != null ? " " + e.SegundoNombre : "") + " " + e.PrimerApellido
                + (e.SegundoApellido != null ? " " + e.SegundoApellido : "") + " " + e.TipoDocumento + " " + e.NumeroDocumento
                + " " + (e.NumeroLicencia ?? "")).Contains(busqueda));
        }
        if (Texto.Opcional(f.Estado) is { } estado)
        {
            var activo = EstadoRegistro.EsActivo(estado);
            consulta = consulta.Where(e => e.Activo == activo);
        }
        var conductores = await consulta.OrdenarPorApellido().ToListAsync(ct);

        var ids = conductores.Select(c => c.Id).ToList();
        var asignaciones = await db.AsignacionesRuta.AsNoTracking()
            .Where(a => a.Activo && ids.Contains(a.IdConductor))
            .Select(a => new
            {
                a.Id, a.IdConductor, Ruta = a.Ruta.Codigo + " · " + a.Ruta.Nombre, a.Ruta.Jornada,
                Vehiculo = a.Vehiculo.Placa + " · Interno " + a.Vehiculo.NumeroInterno + " · " + a.Vehiculo.Marca,
                Monitora = a.Monitora,
            })
            .ToListAsync(ct);

        var hoy = Fechas.Hoy(reloj);
        var diasAlerta = await parametros.DiasAlertaVencimientoAsync(ct);
        var todos = conductores.Select(e =>
        {
            var a = asignaciones.FirstOrDefault(x => x.IdConductor == e.Id);
            return new ConductorDto(
                e.Id, e.NombreCompleto, ReglasPersona.Iniciales(e), $"{e.TipoDocumento} {e.NumeroDocumento}", e.Telefono,
                e.Correo ?? "", e.NumeroLicencia ?? "", e.CategoriaLicencia ?? "", e.FechaVencimientoLicencia,
                Catalogos.EstadoVencimiento(e.FechaVencimientoLicencia, hoy, diasAlerta), EstadoRegistro.Texto(e.Activo),
                a is null ? null : new AsignacionConductorDto(a.Id, a.Ruta, a.Jornada, a.Vehiculo, a.Monitora?.NombreCompleto ?? ""),
                e.Foto ?? "");
        })
        .Where(c => Texto.Opcional(f.Licencia) is not { } licencia || c.EstadoLicencia == licencia)
        .Where(c => Texto.Opcional(f.Asignacion) is not { } asignacion || (asignacion == "CON") == (c.Asignacion is not null))
        .ToList();

        var items = todos.Skip(paginacion.Omitir).Take(paginacion.TamanoValido).ToList();
        return new ListaPaginada<ConductorDto>(items, todos.Count, paginacion.PaginaValida, paginacion.TamanoValido);
    }
}
