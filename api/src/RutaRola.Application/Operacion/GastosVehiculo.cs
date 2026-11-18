using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Application.Finanzas;
using RutaRola.Domain.Enums;
using RutaRola.Domain.Finanzas;

namespace RutaRola.Application.Operacion;

/// <summary>
/// Gasto de un vehículo (<c>operacion/vehiculos/gastos-vehiculo/gasto-vehiculo.ts</c>): tanqueadas, mantenimientos,
/// repuestos, llantas, peajes… <c>cantidad</c> = galones en COMBUSTIBLE. <c>kilometraje</c>/<c>cantidad</c> null = no se
/// indicó. <c>archivo.url</c> va vacía en el listado; el detalle trae el contenido.
/// </summary>
public record GastoVehiculoDto(
    Guid Id, Guid VehiculoId, string Vehiculo, TipoGasto Tipo, DateOnly Fecha, decimal Valor, string Descripcion, string Proveedor,
    string Referencia, int? Kilometraje, decimal? Cantidad, ArchivoAdjuntoDto? Archivo, DateTime FechaCreacion);

public record SolicitudGastoVehiculo(
    Guid? VehiculoId, TipoGasto? Tipo, DateOnly? Fecha, decimal? Valor, string? Descripcion, string? Proveedor, string? Referencia,
    int? Kilometraje, decimal? Cantidad, ArchivoAdjuntoDto? Archivo);

/// <summary><c>desde</c>/<c>hasta</c>: fechas incluidas; <c>texto</c> busca en descripción, proveedor y referencia.</summary>
public record FiltrosGastoVehiculo(
    Guid? VehiculoId = null, TipoGasto? Tipo = null, DateOnly? Desde = null, DateOnly? Hasta = null, string? Texto = null);

public record GastoPorTipoDto(TipoGasto Tipo, decimal Total, int Cantidad);
public record GastoPorMesDto(string Periodo, decimal Total, decimal Combustible);
public record GastoPorVehiculoDto(Guid VehiculoId, string Vehiculo, decimal Total, int Cantidad);

/// <summary>
/// Tablero de gastos del filtro: totales, por tipo, por mes (con la parte de combustible), por vehículo y el
/// rendimiento. <c>kilometrosRecorridos</c> = mayor − menor kilometraje registrado de cada vehículo (sumados);
/// <c>costoPorKilometro</c> y <c>kilometrosPorGalon</c> son null cuando no hay datos para calcularlos.
/// </summary>
public record ResumenGastosDto(
    decimal Total, int Cantidad, decimal PromedioMensual, decimal Combustible, decimal Galones, int? KilometrosRecorridos,
    decimal? CostoPorKilometro, decimal? KilometrosPorGalon, IReadOnlyList<GastoPorTipoDto> PorTipo, IReadOnlyList<GastoPorMesDto> PorMes,
    IReadOnlyList<GastoPorVehiculoDto> PorVehiculo);

public class ValidadorSolicitudGastoVehiculo : AbstractValidator<SolicitudGastoVehiculo>
{
    public ValidadorSolicitudGastoVehiculo()
    {
        RuleFor(x => x.VehiculoId).NotEmpty().WithMessage("Elige el vehículo.");
        RuleFor(x => x.Tipo).NotNull().WithMessage("Elige el tipo de gasto.");
        RuleFor(x => x.Fecha).NotNull().WithMessage("Ingresa la fecha del gasto.");
        RuleFor(x => x.Valor).NotNull().WithMessage("Ingresa el valor.")
            .GreaterThan(0).WithMessage("El valor debe ser mayor que cero.")
            .LessThan(10_000_000_000m).WithMessage("El valor es demasiado alto.");
        RuleFor(x => x.Descripcion).MaximumLength(300).WithMessage("La descripción admite máximo 300 caracteres.");
        RuleFor(x => x.Proveedor).MaximumLength(120).WithMessage("El proveedor admite máximo 120 caracteres.");
        RuleFor(x => x.Referencia).MaximumLength(50).WithMessage("La referencia admite máximo 50 caracteres.");
        RuleFor(x => x.Kilometraje).InclusiveBetween(0, 5_000_000).When(x => x.Kilometraje is not null)
            .WithMessage("El kilometraje no es válido.");
        RuleFor(x => x.Cantidad).GreaterThan(0).LessThan(100_000).When(x => x.Cantidad is not null)
            .WithMessage("La cantidad no es válida.");
        RuleFor(x => x.Cantidad).NotNull().When(x => x.Tipo == TipoGasto.Combustible)
            .WithMessage("Ingresa los galones tanqueados.");
        When(x => x.Archivo is not null, () => RuleFor(x => x.Archivo!).ReglasAdjunto("soporte"));
    }
}

/// <summary>Historial de gastos por vehículo y su tablero (Operación › Vehículos › Gastos).</summary>
public class ServicioGastosVehiculo(IBaseDatos db, IContextoUsuario contexto, TimeProvider reloj)
    : ServicioCrud<Gasto, GastoVehiculoDto, SolicitudGastoVehiculo, FiltrosGastoVehiculo>(db)
{
    protected override string Entidad => "El gasto";
    protected override DbSet<Gasto> Conjunto => Db.Gastos;

    protected override IQueryable<Gasto> Filtrar(IQueryable<Gasto> consulta, FiltrosGastoVehiculo f)
    {
        consulta = consulta.Where(g => g.IdVehiculo != null);
        if (f.VehiculoId is { } vehiculo) consulta = consulta.Where(g => g.IdVehiculo == vehiculo);
        if (f.Tipo is { } tipo) consulta = consulta.Where(g => g.Tipo == tipo);
        if (f.Desde is { } desde) consulta = consulta.Where(g => g.FechaGasto >= desde);
        if (f.Hasta is { } hasta) consulta = consulta.Where(g => g.FechaGasto <= hasta);
        if (Texto.Opcional(f.Texto) is { } texto)
        {
            var busqueda = Busqueda.Normalizar(texto);
            consulta = consulta.Where(g =>
                FuncionesBd.SinTildes((g.Descripcion ?? "") + " " + (g.Proveedor ?? "") + " " + (g.Referencia ?? "")).Contains(busqueda));
        }
        return consulta;
    }

    protected override IOrderedQueryable<Gasto> Ordenar(IQueryable<Gasto> consulta) =>
        consulta.OrderByDescending(g => g.FechaGasto).ThenByDescending(g => g.FechaCreacion).ThenBy(g => g.Id);

    protected override IQueryable<Gasto> Incluir(IQueryable<Gasto> consulta) => consulta.Include(g => g.Vehiculo);

    protected override string Etiqueta(Gasto g) => $"{TextoEnum.Escribir(g.Tipo)} · {g.FechaGasto:yyyy-MM-dd} · {Cobro.Pesos(g.Valor)}";

    protected override async Task<IReadOnlyList<GastoVehiculoDto>> MapearAsync(IReadOnlyList<Gasto> registros, bool detalle, CancellationToken ct)
    {
        var idsArchivos = registros.Where(g => g.IdArchivo != null).Select(g => g.IdArchivo!.Value).ToList();
        var archivos = new Dictionary<Guid, ArchivoAdjuntoDto>();
        if (idsArchivos.Count > 0)
        {
            if (detalle)
            {
                foreach (var a in await Db.Archivos.AsNoTracking().Where(a => idsArchivos.Contains(a.Id)).ToListAsync(ct))
                {
                    archivos[a.Id] = Adjuntos.Dto(a, true);
                }
            }
            else
            {
                archivos = await Db.Archivos.AsNoTracking().Where(a => idsArchivos.Contains(a.Id))
                    .Select(a => new { a.Id, a.Nombre, a.TipoArchivo, a.Tamano })
                    .ToDictionaryAsync(a => a.Id, a => new ArchivoAdjuntoDto(a.Nombre, a.TipoArchivo, a.Tamano, ""), ct);
            }
        }
        return registros.Select(g => new GastoVehiculoDto(
            g.Id, g.IdVehiculo!.Value, g.Vehiculo is { } v ? $"{v.Placa} · Interno {v.NumeroInterno}" : "", g.Tipo, g.FechaGasto, g.Valor,
            g.Descripcion ?? "", g.Proveedor ?? "", g.Referencia ?? "", g.Kilometraje, g.Cantidad,
            g.IdArchivo is { } id ? archivos.GetValueOrDefault(id) : null, g.FechaCreacion)).ToList();
    }

    protected override async Task AplicarAsync(Gasto g, SolicitudGastoVehiculo s, bool nuevo, CancellationToken ct)
    {
        if (!await Db.Vehiculos.AnyAsync(v => v.Id == s.VehiculoId, ct))
        {
            throw ErrorNegocio.NoEncontrado("El vehículo");
        }
        if (s.Fecha > Fechas.Hoy(reloj))
        {
            throw new ErrorNegocio("La fecha del gasto no puede ser futura.", CodigoError.Validacion);
        }
        g.IdVehiculo = s.VehiculoId;
        g.Tipo = s.Tipo!.Value;
        g.FechaGasto = s.Fecha!.Value;
        g.Valor = Math.Round(s.Valor!.Value, 2);
        g.Descripcion = Texto.Opcional(s.Descripcion);
        g.Proveedor = Texto.Opcional(s.Proveedor);
        g.Referencia = Texto.Opcional(s.Referencia);
        g.Kilometraje = s.Kilometraje;
        g.Cantidad = s.Cantidad is { } c ? Math.Round(c, 2) : null;
        if (nuevo) g.CreadoPor = contexto.IdUsuario;
        g.IdArchivo = await Adjuntos.AplicarAsync(Db, g.IdArchivo, s.Archivo, contexto.IdUsuario, "soporte", ct);
    }

    public override Task<string?> MotivoNoEliminarAsync(Guid id, CancellationToken ct = default) => Task.FromResult<string?>(null);

    protected override Task AntesDeEliminarAsync(Gasto registro, CancellationToken ct) => Adjuntos.EliminarAsync(Db, registro.IdArchivo, ct);

    public async Task<ResumenGastosDto> ResumenAsync(FiltrosGastoVehiculo f, CancellationToken ct = default)
    {
        var gastos = await Filtrar(Db.Gastos.AsNoTracking(), f)
            .Select(g => new GastoResumible(g.IdVehiculo!.Value, g.Tipo, g.FechaGasto, g.Valor, g.Kilometraje, g.Cantidad))
            .ToListAsync(ct);
        var ids = gastos.Select(g => g.VehiculoId).Distinct().ToList();
        var nombres = await Db.Vehiculos.AsNoTracking().Where(v => ids.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => $"{v.Placa} · Interno {v.NumeroInterno}", ct);
        return ReglasGastos.Resumir(gastos, nombres);
    }
}

public record GastoResumible(Guid VehiculoId, TipoGasto Tipo, DateOnly Fecha, decimal Valor, int? Kilometraje, decimal? Cantidad);

/// <summary>Cálculo puro del tablero (igual que <c>resumirGastos</c> de <c>gasto-vehiculo.ts</c>).</summary>
public static class ReglasGastos
{
    public static ResumenGastosDto Resumir(IReadOnlyList<GastoResumible> gastos, IReadOnlyDictionary<Guid, string> nombres)
    {
        var total = gastos.Sum(g => g.Valor);
        var combustible = gastos.Where(g => g.Tipo == TipoGasto.Combustible).ToList();
        var galones = combustible.Sum(g => g.Cantidad ?? 0);

        var porMes = gastos.GroupBy(g => new DateOnly(g.Fecha.Year, g.Fecha.Month, 1)).OrderBy(g => g.Key)
            .Select(g => new GastoPorMesDto(Periodos.Texto(g.Key), g.Sum(x => x.Valor),
                g.Where(x => x.Tipo == TipoGasto.Combustible).Sum(x => x.Valor)))
            .ToList();
        var meses = gastos.Count == 0 ? 0 : MesesEntre(gastos.Min(g => g.Fecha), gastos.Max(g => g.Fecha));

        // Kilómetros: por vehículo, del menor al mayor kilometraje registrado.
        var kilometros = 0;
        var hayKilometros = false;
        decimal galonesConKm = 0;
        foreach (var grupo in gastos.Where(g => g.Kilometraje is not null).GroupBy(g => g.VehiculoId))
        {
            var lecturas = grupo.OrderBy(g => g.Kilometraje).ToList();
            if (lecturas.Count < 2) continue;
            var recorrido = lecturas[^1].Kilometraje!.Value - lecturas[0].Kilometraje!.Value;
            if (recorrido <= 0) continue;
            hayKilometros = true;
            kilometros += recorrido;
            // Rendimiento: galones tanqueados después de la primera lectura (el primer tanqueo llena hasta ahí).
            galonesConKm += lecturas.Skip(1).Where(g => g.Tipo == TipoGasto.Combustible).Sum(g => g.Cantidad ?? 0);
        }

        return new ResumenGastosDto(
            total,
            gastos.Count,
            meses == 0 ? 0 : Math.Round(total / meses, 0),
            combustible.Sum(g => g.Valor),
            galones,
            hayKilometros ? kilometros : null,
            hayKilometros ? Math.Round(total / kilometros, 0) : null,
            hayKilometros && galonesConKm > 0 ? Math.Round(kilometros / galonesConKm, 1) : null,
            gastos.GroupBy(g => g.Tipo).Select(g => new GastoPorTipoDto(g.Key, g.Sum(x => x.Valor), g.Count()))
                .OrderByDescending(g => g.Total).ToList(),
            porMes,
            gastos.GroupBy(g => g.VehiculoId)
                .Select(g => new GastoPorVehiculoDto(g.Key, nombres.GetValueOrDefault(g.Key, ""), g.Sum(x => x.Valor), g.Count()))
                .OrderByDescending(g => g.Total).ToList());
    }

    /// <summary>Meses calendario que abarca el rango (ambos incluidos): ene–mar = 3.</summary>
    public static int MesesEntre(DateOnly desde, DateOnly hasta) => (hasta.Year - desde.Year) * 12 + hasta.Month - desde.Month + 1;
}
