using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Domain.Enums;
using RutaRola.Domain.Operacion;

namespace RutaRola.Application.Operacion;

/// <summary>
/// Vehículo como lo usa el frontend: <c>modelo</c> = año, <c>linea</c> = línea y las cuatro fechas de
/// vencimiento planas (en la base de datos son filas de <c>documentos_vehiculo</c>).
/// </summary>
public record VehiculoDto(
    Guid Id, string Placa, string NumeroInterno, TipoVehiculo Tipo, string Marca, string Linea, int Modelo, string Color,
    int Capacidad, DateOnly? VencimientoSoat, DateOnly? VencimientoTecnomecanica, DateOnly? VencimientoTarjetaOperacion,
    DateOnly? VencimientoPolizas, string Estado, string Observaciones, DateTime FechaCreacion, DateTime? FechaActualizacion);

public record SolicitudVehiculo(
    string? Placa, string? NumeroInterno, TipoVehiculo? Tipo, string? Marca, string? Linea, int? Modelo, string? Color,
    int? Capacidad, DateOnly? VencimientoSoat, DateOnly? VencimientoTecnomecanica, DateOnly? VencimientoTarjetaOperacion,
    DateOnly? VencimientoPolizas, string? Estado, string? Observaciones);

/// <summary><c>texto</c> busca en placa, número interno, marca y línea.</summary>
public record FiltrosVehiculo(string? Texto = null, TipoVehiculo? Tipo = null, string? Estado = null);

public class ValidadorSolicitudVehiculo : AbstractValidator<SolicitudVehiculo>
{
    public ValidadorSolicitudVehiculo()
    {
        RuleFor(x => x.Placa).NotEmpty().WithMessage("Ingresa la placa.")
            .Must(p => p is null || ServicioVehiculos.NormalizarPlaca(p).Length == 6 &&
                System.Text.RegularExpressions.Regex.IsMatch(ServicioVehiculos.NormalizarPlaca(p), "^[A-Z]{3}[0-9]{3}$"))
            .WithMessage("La placa debe tener 3 letras y 3 números (ej. ABC123).");
        RuleFor(x => x.NumeroInterno).NotEmpty().WithMessage("Ingresa el número interno.")
            .Matches("^[A-Za-z0-9-]{1,10}$").WithMessage("El número interno admite hasta 10 letras, números o guiones.");
        RuleFor(x => x.Tipo).NotNull().WithMessage("Selecciona el tipo de vehículo.");
        RuleFor(x => x.Marca).NotEmpty().WithMessage("Ingresa la marca.")
            .MaximumLength(40).WithMessage("La marca admite máximo 40 caracteres.");
        RuleFor(x => x.Linea).MaximumLength(40).WithMessage("La línea admite máximo 40 caracteres.");
        RuleFor(x => x.Modelo).NotNull().WithMessage("Ingresa el modelo (año).")
            .InclusiveBetween(1990, DateTime.UtcNow.Year + 1).WithMessage($"El modelo debe estar entre 1990 y {DateTime.UtcNow.Year + 1}.");
        RuleFor(x => x.Color).MaximumLength(30).WithMessage("El color admite máximo 30 caracteres.");
        RuleFor(x => x.Capacidad).NotNull().WithMessage("Ingresa la capacidad.")
            .InclusiveBetween(1, 80).WithMessage("La capacidad debe estar entre 1 y 80 puestos.");
        RuleFor(x => x.VencimientoSoat).NotNull().WithMessage("Ingresa el vencimiento del SOAT.");
        RuleFor(x => x.VencimientoTecnomecanica).NotNull().WithMessage("Ingresa el vencimiento de la revisión técnico-mecánica.");
        RuleFor(x => x.VencimientoTarjetaOperacion).NotNull().WithMessage("Ingresa el vencimiento de la tarjeta de operación.");
        RuleFor(x => x.VencimientoPolizas).NotNull().WithMessage("Ingresa el vencimiento de las pólizas.");
        RuleFor(x => x.Estado).EstadoRegistroValido();
        RuleFor(x => x.Observaciones).MaximumLength(500).WithMessage("Las observaciones admiten máximo 500 caracteres.");
    }
}

/// <summary>Vehículos con sus documentos. Placa y número interno únicos; no se elimina si tiene asignaciones.</summary>
public class ServicioVehiculos(IBaseDatos db, Suscripciones.ServicioSuscripcion suscripcion) : ServicioCrud<Vehiculo, VehiculoDto, SolicitudVehiculo, FiltrosVehiculo>(db)
{
    protected override string Entidad => "El vehículo";
    protected override DbSet<Vehiculo> Conjunto => Db.Vehiculos;

    /// <summary>Mayúsculas, sin espacios ni guiones (igual que el formulario).</summary>
    public static string NormalizarPlaca(string placa) => placa.Replace(" ", "").Replace("-", "").ToUpperInvariant();

    /// <summary><c>ABC123 · Interno 101 · Chevrolet</c> (<c>etiquetaVehiculo</c>).</summary>
    public static string Etiquetar(Vehiculo v) => $"{v.Placa} · Interno {v.NumeroInterno} · {v.Marca}";

    protected override IQueryable<Vehiculo> Filtrar(IQueryable<Vehiculo> consulta, FiltrosVehiculo f)
    {
        if (Texto.Opcional(f.Texto) is { } texto)
        {
            var busqueda = Busqueda.Normalizar(texto);
            consulta = consulta.Where(v =>
                FuncionesBd.SinTildes(v.Placa + " " + v.NumeroInterno + " " + v.Marca + " " + (v.Linea ?? "")).Contains(busqueda));
        }
        if (f.Tipo is { } tipo)
        {
            consulta = consulta.Where(v => v.Tipo == tipo);
        }
        if (Texto.Opcional(f.Estado) is { } estado)
        {
            var activo = EstadoRegistro.EsActivo(estado);
            consulta = consulta.Where(v => v.Activo == activo);
        }
        return consulta;
    }

    /// <summary>Número interno en orden numérico ("2" antes de "10").</summary>
    protected override IOrderedQueryable<Vehiculo> Ordenar(IQueryable<Vehiculo> consulta) =>
        consulta.OrderBy(v => v.NumeroInterno.Length).ThenBy(v => v.NumeroInterno).ThenBy(v => v.Id);

    protected override IQueryable<Vehiculo> Incluir(IQueryable<Vehiculo> consulta) => consulta.Include(v => v.Documentos);

    protected override string Etiqueta(Vehiculo v) => Etiquetar(v);

    protected override Task<IReadOnlyList<VehiculoDto>> MapearAsync(IReadOnlyList<Vehiculo> registros, bool detalle, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<VehiculoDto>>(registros.Select(v =>
        {
            DateOnly? Vence(TipoDocumentoVehiculo tipo) => v.Documentos.FirstOrDefault(d => d.TipoDocumento == tipo)?.FechaVencimiento;
            return new VehiculoDto(
                v.Id, v.Placa, v.NumeroInterno, v.Tipo, v.Marca, v.Linea ?? "", v.Anio, v.Color ?? "", v.Capacidad,
                Vence(TipoDocumentoVehiculo.Soat), Vence(TipoDocumentoVehiculo.Tecnomecanica),
                Vence(TipoDocumentoVehiculo.TarjetaOperacion), Vence(TipoDocumentoVehiculo.Polizas),
                EstadoRegistro.Texto(v.Activo), v.Observaciones ?? "", v.FechaCreacion, v.FechaActualizacion);
        }).ToList());

    protected override async Task AplicarAsync(Vehiculo v, SolicitudVehiculo s, bool nuevo, CancellationToken ct)
    {
        var placa = NormalizarPlaca(s.Placa!);
        var interno = s.NumeroInterno!.Trim();
        var idActual = nuevo ? (Guid?)null : v.Id;
        if (await Db.Vehiculos.AnyAsync(x => x.Id != idActual && x.Placa == placa, ct))
        {
            throw new ErrorNegocio($"Ya existe un vehículo con la placa {placa}.", CodigoError.Duplicado);
        }
        if (await Db.Vehiculos.AnyAsync(x => x.Id != idActual && x.NumeroInterno == interno, ct))
        {
            throw new ErrorNegocio($"El número interno {interno} ya está asignado a otro vehículo.", CodigoError.Duplicado);
        }

        v.Placa = placa;
        v.NumeroInterno = interno;
        v.Tipo = s.Tipo!.Value;
        v.Marca = s.Marca!.Trim();
        v.Linea = Texto.Opcional(s.Linea);
        v.Anio = s.Modelo!.Value;
        v.Color = Texto.Opcional(s.Color);
        v.Capacidad = s.Capacidad!.Value;
        // Activar uno más no puede superar el límite del plan (los ya activos se conservan).
        if (EstadoRegistro.EsActivo(s.Estado) && (nuevo || !v.Activo))
        {
            await suscripcion.ValidarVehiculoAsync(nuevo ? null : v.Id, ct);
        }
        v.Activo = EstadoRegistro.EsActivo(s.Estado);
        v.Observaciones = Texto.Opcional(s.Observaciones);

        Documento(v, TipoDocumentoVehiculo.Soat, s.VencimientoSoat!.Value);
        Documento(v, TipoDocumentoVehiculo.Tecnomecanica, s.VencimientoTecnomecanica!.Value);
        Documento(v, TipoDocumentoVehiculo.TarjetaOperacion, s.VencimientoTarjetaOperacion!.Value);
        Documento(v, TipoDocumentoVehiculo.Polizas, s.VencimientoPolizas!.Value);
    }

    private static void Documento(Vehiculo v, TipoDocumentoVehiculo tipo, DateOnly vencimiento)
    {
        var documento = v.Documentos.FirstOrDefault(d => d.TipoDocumento == tipo);
        if (documento is null)
        {
            documento = new DocumentoVehiculo { TipoDocumento = tipo };
            v.Documentos.Add(documento);
        }
        documento.FechaVencimiento = vencimiento;
    }

    public override async Task<string?> MotivoNoEliminarAsync(Guid id, CancellationToken ct = default)
    {
        var total = await Db.AsignacionesRuta.CountAsync(a => a.IdVehiculo == id, ct);
        return total > 0
            ? $"No se puede eliminar: el vehículo tiene {total} asignación(es) de ruta. Cámbialo a Inactivo para conservar el historial."
            : null;
    }
}
