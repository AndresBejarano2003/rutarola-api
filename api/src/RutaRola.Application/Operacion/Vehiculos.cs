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
    DateOnly? VencimientoPolizas, string Estado, string Observaciones, DateTime FechaCreacion, DateTime? FechaActualizacion,
    string Foto = "");

/// <summary><c>foto</c>: data URL JPG/PNG/WebP o <c>""</c> (sin foto).</summary>
public record SolicitudVehiculo(
    string? Placa, string? NumeroInterno, TipoVehiculo? Tipo, string? Marca, string? Linea, int? Modelo, string? Color,
    int? Capacidad, DateOnly? VencimientoSoat, DateOnly? VencimientoTecnomecanica, DateOnly? VencimientoTarjetaOperacion,
    DateOnly? VencimientoPolizas, string? Estado, string? Observaciones, string? Foto = null);

/// <summary>
/// Un documento del historial del vehículo (SOAT, técnico-mecánica, tarjeta de operación, pólizas u OTRO) con su PDF.
/// <c>vigente</c>: es el de vencimiento más reciente de su tipo (el que manda en el vehículo). <c>archivo.url</c> va
/// vacía en el listado; el detalle trae el contenido.
/// </summary>
public record DocumentoHistorialDto(
    Guid Id, Guid VehiculoId, TipoDocumentoVehiculo Tipo, string Numero, string Entidad, DateOnly? FechaExpedicion, DateOnly FechaVencimiento,
    string Observaciones, ArchivoAdjuntoDto? Archivo, bool Vigente, DateTime FechaCreacion);

public record SolicitudDocumentoHistorial(
    TipoDocumentoVehiculo? Tipo, string? Numero, string? Entidad, DateOnly? FechaExpedicion, DateOnly? FechaVencimiento, string? Observaciones,
    ArchivoAdjuntoDto? Archivo);

public class ValidadorSolicitudDocumentoHistorial : AbstractValidator<SolicitudDocumentoHistorial>
{
    public ValidadorSolicitudDocumentoHistorial()
    {
        RuleFor(x => x.Tipo).NotNull().WithMessage("Elige el tipo de documento.");
        RuleFor(x => x.Numero).MaximumLength(50).WithMessage("El número admite máximo 50 caracteres.");
        RuleFor(x => x.Entidad).MaximumLength(100).WithMessage("La entidad admite máximo 100 caracteres.");
        RuleFor(x => x.FechaVencimiento).NotNull().WithMessage("Ingresa la fecha de vencimiento.");
        RuleFor(x => x.FechaVencimiento).GreaterThanOrEqualTo(x => x.FechaExpedicion)
            .When(x => x.FechaExpedicion is not null && x.FechaVencimiento is not null)
            .WithMessage("El vencimiento debe ser posterior a la expedición.");
        RuleFor(x => x.Observaciones).MaximumLength(500).WithMessage("Las observaciones admiten máximo 500 caracteres.");
        When(x => x.Archivo is not null, () => RuleFor(x => x.Archivo!).ReglasAdjunto("documento"));
    }
}

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
        RuleFor(x => x.Foto).FotoValida();
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
                EstadoRegistro.Texto(v.Activo), v.Observaciones ?? "", v.FechaCreacion, v.FechaActualizacion, v.Foto ?? "");
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
        v.Foto = Fotos.Normalizar(s.Foto);

        var historial = nuevo
            ? []
            : await Db.HistorialDocumentosVehiculo.Where(h => h.IdVehiculo == v.Id).Select(h => new { h.TipoDocumento, h.FechaVencimiento }).ToListAsync(ct);
        void Documento(TipoDocumentoVehiculo tipo, DateOnly vencimiento)
        {
            var documento = v.Documentos.FirstOrDefault(d => d.TipoDocumento == tipo);
            if (documento is null)
            {
                documento = new DocumentoVehiculo { TipoDocumento = tipo };
                v.Documentos.Add(documento);
            }
            documento.FechaVencimiento = vencimiento;
            // Cada vencimiento nuevo queda en el historial (sin PDF: se le puede adjuntar después).
            if (!historial.Any(h => h.TipoDocumento == tipo && h.FechaVencimiento == vencimiento))
            {
                Db.HistorialDocumentosVehiculo.Add(new HistorialDocumentoVehiculo { IdVehiculo = v.Id, TipoDocumento = tipo, FechaVencimiento = vencimiento });
            }
        }
        Documento(TipoDocumentoVehiculo.Soat, s.VencimientoSoat!.Value);
        Documento(TipoDocumentoVehiculo.Tecnomecanica, s.VencimientoTecnomecanica!.Value);
        Documento(TipoDocumentoVehiculo.TarjetaOperacion, s.VencimientoTarjetaOperacion!.Value);
        Documento(TipoDocumentoVehiculo.Polizas, s.VencimientoPolizas!.Value);
    }

    /// <summary>El historial se borra en cascada; sus PDF se borran aquí.</summary>
    protected override async Task AntesDeEliminarAsync(Vehiculo registro, CancellationToken ct)
    {
        var archivos = await Db.HistorialDocumentosVehiculo.Where(h => h.IdVehiculo == registro.Id && h.IdArchivo != null)
            .Select(h => h.IdArchivo).ToListAsync(ct);
        foreach (var id in archivos)
        {
            await Adjuntos.EliminarAsync(Db, id, ct);
        }
    }

    public override async Task<string?> MotivoNoEliminarAsync(Guid id, CancellationToken ct = default)
    {
        var total = await Db.AsignacionesRuta.CountAsync(a => a.IdVehiculo == id, ct);
        return total > 0
            ? $"No se puede eliminar: el vehículo tiene {total} asignación(es) de ruta. Cámbialo a Inactivo para conservar el historial."
            : null;
    }
}

/// <summary>
/// Historial de documentos de un vehículo (<c>/vehiculos/{id}/documentos</c>). Cada renovación queda como un registro
/// con su PDF o imagen (≤ 2 MB). Al guardar o eliminar uno, el vencimiento del vehículo para ese tipo pasa a ser el más
/// reciente del historial (salvo OTRO, que no tiene vencimiento en el vehículo).
/// </summary>
public class ServicioDocumentosVehiculo(IBaseDatos db, IContextoUsuario contexto, TimeProvider reloj)
{
    public async Task<IReadOnlyList<DocumentoHistorialDto>> ListarAsync(Guid idVehiculo, CancellationToken ct = default)
    {
        await VehiculoAsync(idVehiculo, ct);
        var registros = await db.HistorialDocumentosVehiculo.AsNoTracking().Include(h => h.Archivo)
            .Where(h => h.IdVehiculo == idVehiculo).OrderByDescending(h => h.FechaVencimiento).ThenByDescending(h => h.FechaCreacion).ToListAsync(ct);
        var vigentes = Vigentes(registros);
        return registros.Select(h => Dto(h, vigentes.Contains(h.Id), false)).ToList();
    }

    public async Task<DocumentoHistorialDto> ObtenerAsync(Guid idVehiculo, Guid id, CancellationToken ct = default)
    {
        var registros = await db.HistorialDocumentosVehiculo.AsNoTracking().Include(h => h.Archivo)
            .Where(h => h.IdVehiculo == idVehiculo).ToListAsync(ct);
        var registro = registros.FirstOrDefault(h => h.Id == id) ?? throw ErrorNegocio.NoEncontrado("El documento");
        return Dto(registro, Vigentes(registros).Contains(id), true);
    }

    public async Task<DocumentoHistorialDto> CrearAsync(Guid idVehiculo, SolicitudDocumentoHistorial s, CancellationToken ct = default)
    {
        var vehiculo = await VehiculoAsync(idVehiculo, ct);
        var registro = new HistorialDocumentoVehiculo { IdVehiculo = idVehiculo };
        db.HistorialDocumentosVehiculo.Add(registro);
        await AplicarAsync(vehiculo, registro, s, ct);
        return await ObtenerAsync(idVehiculo, registro.Id, ct);
    }

    public async Task<DocumentoHistorialDto> ActualizarAsync(Guid idVehiculo, Guid id, SolicitudDocumentoHistorial s, CancellationToken ct = default)
    {
        var vehiculo = await VehiculoAsync(idVehiculo, ct);
        var registro = await db.HistorialDocumentosVehiculo.FirstOrDefaultAsync(h => h.Id == id && h.IdVehiculo == idVehiculo, ct)
            ?? throw ErrorNegocio.NoEncontrado("El documento");
        var tipoAnterior = registro.TipoDocumento;
        await AplicarAsync(vehiculo, registro, s, ct, tipoAnterior);
        return await ObtenerAsync(idVehiculo, id, ct);
    }

    public async Task EliminarAsync(Guid idVehiculo, Guid id, CancellationToken ct = default)
    {
        var vehiculo = await VehiculoAsync(idVehiculo, ct);
        var registro = await db.HistorialDocumentosVehiculo.FirstOrDefaultAsync(h => h.Id == id && h.IdVehiculo == idVehiculo, ct)
            ?? throw ErrorNegocio.NoEncontrado("El documento");
        await Adjuntos.EliminarAsync(db, registro.IdArchivo, ct);
        db.HistorialDocumentosVehiculo.Remove(registro);
        await db.SaveChangesAsync(ct);
        await SincronizarAsync(vehiculo, registro.TipoDocumento, ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task AplicarAsync(
        Vehiculo vehiculo, HistorialDocumentoVehiculo h, SolicitudDocumentoHistorial s, CancellationToken ct, TipoDocumentoVehiculo? tipoAnterior = null)
    {
        h.TipoDocumento = s.Tipo!.Value;
        h.NumeroDocumento = Texto.Opcional(s.Numero);
        h.Entidad = Texto.Opcional(s.Entidad);
        h.FechaExpedicion = s.FechaExpedicion;
        h.FechaVencimiento = s.FechaVencimiento!.Value;
        h.Observaciones = Texto.Opcional(s.Observaciones);
        h.IdArchivo = await Adjuntos.AplicarAsync(db, h.IdArchivo, s.Archivo, contexto.IdUsuario, "documento", ct);
        await db.SaveChangesAsync(ct);
        await SincronizarAsync(vehiculo, h.TipoDocumento, ct);
        if (tipoAnterior is { } anterior && anterior != h.TipoDocumento)
        {
            await SincronizarAsync(vehiculo, anterior, ct);
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>El vencimiento del vehículo para el tipo = el más reciente del historial (si queda alguno).</summary>
    private async Task SincronizarAsync(Vehiculo vehiculo, TipoDocumentoVehiculo tipo, CancellationToken ct)
    {
        if (tipo == TipoDocumentoVehiculo.Otro) return;
        var ultimo = await db.HistorialDocumentosVehiculo.Where(h => h.IdVehiculo == vehiculo.Id && h.TipoDocumento == tipo)
            .OrderByDescending(h => h.FechaVencimiento).Select(h => (DateOnly?)h.FechaVencimiento).FirstOrDefaultAsync(ct);
        if (ultimo is not { } fecha) return;
        var documento = vehiculo.Documentos.FirstOrDefault(d => d.TipoDocumento == tipo);
        if (documento is null)
        {
            documento = new DocumentoVehiculo { TipoDocumento = tipo };
            vehiculo.Documentos.Add(documento);
        }
        documento.FechaVencimiento = fecha;
        vehiculo.FechaActualizacion = reloj.GetUtcNow().UtcDateTime;
    }

    private async Task<Vehiculo> VehiculoAsync(Guid id, CancellationToken ct) =>
        await db.Vehiculos.Include(v => v.Documentos).FirstOrDefaultAsync(v => v.Id == id, ct) ?? throw ErrorNegocio.NoEncontrado("El vehículo");

    /// <summary>Ids de los registros con el vencimiento más reciente de cada tipo.</summary>
    private static HashSet<Guid> Vigentes(IEnumerable<HistorialDocumentoVehiculo> registros) =>
        registros.Where(h => h.TipoDocumento != TipoDocumentoVehiculo.Otro).GroupBy(h => h.TipoDocumento)
            .Select(g => g.OrderByDescending(h => h.FechaVencimiento).ThenByDescending(h => h.FechaCreacion).First().Id).ToHashSet();

    private static DocumentoHistorialDto Dto(HistorialDocumentoVehiculo h, bool vigente, bool conContenido) => new(
        h.Id, h.IdVehiculo, h.TipoDocumento, h.NumeroDocumento ?? "", h.Entidad ?? "", h.FechaExpedicion, h.FechaVencimiento, h.Observaciones ?? "",
        h.Archivo is { } a ? Adjuntos.Dto(a, conContenido) : null, vigente, h.FechaCreacion);
}
