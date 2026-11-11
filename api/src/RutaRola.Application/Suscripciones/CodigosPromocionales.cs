using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Domain.Enums;
using RutaRola.Domain.Suscripciones;

namespace RutaRola.Application.Suscripciones;

/// <summary>
/// Código promocional como lo ve la plataforma. <c>periodo</c> <c>""</c> = mensual y anual; <c>planes</c> vacío = todos
/// los de pago; <c>usosMaximos</c> null = sin límite. <c>vigencia</c>: PROGRAMADO (aún no empieza), VIGENTE o VENCIDO.
/// </summary>
public record CodigoPromocionalDto(
    Guid Id, string Codigo, string Descripcion, TipoDescuento TipoDescuento, decimal Valor, DateOnly FechaInicio, DateOnly FechaFin,
    AlcanceCodigo Alcance, IReadOnlyList<PlanSuscripcion> Planes, string Periodo, int? UsosMaximos, int UsosPorEmpresa, string Estado,
    IReadOnlyList<Guid> EmpresaIds, IReadOnlyList<Opcion> Empresas, int Usos, string Vigencia, DateTime FechaCreacion);

/// <summary>
/// <c>codigo</c> vacío = el sistema lo genera (al crear; al editar se conserva el actual) con el <c>prefijo</c> opcional:
/// <c>REGRESO-7KQ9M2XT</c> o <c>7KQ9-M2XT</c>.
/// </summary>
public record SolicitudCodigoPromocional(
    string? Codigo, string? Descripcion, TipoDescuento? TipoDescuento, decimal? Valor, DateOnly? FechaInicio, DateOnly? FechaFin,
    AlcanceCodigo? Alcance, IReadOnlyList<PlanSuscripcion>? Planes, PeriodoPago? Periodo, int? UsosMaximos, int? UsosPorEmpresa,
    string? Estado, IReadOnlyList<Guid>? EmpresaIds, string? Prefijo = null);

/// <summary><c>vigencia</c>: PROGRAMADO, VIGENTE o VENCIDO.</summary>
public record FiltrosCodigoPromocional(string? Texto = null, string? Estado = null, string? Vigencia = null);

/// <summary>Un uso: empresa, plan, descuento y la referencia del pago (vacía si la plataforma lo activó a mano).</summary>
public record UsoCodigoDto(Guid Id, Guid EmpresaId, string Empresa, PlanSuscripcion Plan, PeriodoPago Periodo, long Descuento, string Referencia, DateTime Fecha);

/// <summary>El código aplica: precio del periodo, descuento (solo sobre el primer mes) y total a pagar, en pesos.</summary>
public record ValidacionCodigoDto(
    string Codigo, string Descripcion, TipoDescuento TipoDescuento, decimal Valor, PlanSuscripcion Plan, PeriodoPago Periodo,
    long Precio, long Descuento, long Total);

public record SolicitudValidacionCodigo(string? Codigo, PlanSuscripcion? Plan, PeriodoPago? Periodo);

public static class ReglasCodigoPromocional
{
    public const string Vigente = "VIGENTE";
    public const string Programado = "PROGRAMADO";
    public const string Vencido = "VENCIDO";
    public const string PatronCodigo = "^[A-Z0-9-]{4,30}$";
    public const string PatronPrefijo = "^[A-Z0-9]{0,12}$";
    /// <summary>Sin 0/O ni 1/I/L para que se pueda dictar y escribir sin confundirse.</summary>
    private const string Alfabeto = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    /// <summary>Código aleatorio (criptográfico, 8 caracteres ≈ 40 bits): <c>PREFIJO-XXXXXXXX</c> o <c>XXXX-XXXX</c>.</summary>
    public static string Generar(string? prefijo)
    {
        var azar = System.Security.Cryptography.RandomNumberGenerator.GetString(Alfabeto, 8);
        var p = Normalizar(prefijo);
        return p.Length > 0 ? $"{p}-{azar}" : $"{azar[..4]}-{azar[4..]}";
    }

    public static string Normalizar(string? codigo) => (codigo ?? "").Trim().ToUpperInvariant();

    public static string Vigencia(DateOnly inicio, DateOnly fin, DateOnly hoy) => hoy < inicio ? Programado : hoy > fin ? Vencido : Vigente;

    /// <summary>
    /// Descuento en pesos: SOLO sobre el valor de un mes del plan (también en el anual) y nunca mayor que el total.
    /// Porcentaje redondeado al peso.
    /// </summary>
    public static long Descuento(TipoDescuento tipo, decimal valor, long precioMensual, long total)
    {
        var descuento = tipo == TipoDescuento.Porcentaje
            ? (long)Math.Round(precioMensual * Math.Clamp(valor, 0, 100) / 100m, MidpointRounding.AwayFromZero)
            : (long)Math.Min(Math.Max(0, Math.Round(valor)), precioMensual);
        return Math.Min(descuento, total);
    }

    public static IReadOnlyList<PlanSuscripcion> LeerPlanes(string planes) =>
        planes.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(p => TextoEnum.Leer<PlanSuscripcion>(p)).OfType<PlanSuscripcion>().ToList();

    public static string EscribirPlanes(IEnumerable<PlanSuscripcion>? planes) =>
        string.Join(',', (planes ?? []).Distinct().Order().Select(p => TextoEnum.Escribir(p)));
}

public class ValidadorSolicitudCodigoPromocional : AbstractValidator<SolicitudCodigoPromocional>
{
    public ValidadorSolicitudCodigoPromocional()
    {
        RuleFor(x => x.Codigo)
            .Must(c => System.Text.RegularExpressions.Regex.IsMatch(ReglasCodigoPromocional.Normalizar(c), ReglasCodigoPromocional.PatronCodigo))
            .When(x => !string.IsNullOrWhiteSpace(x.Codigo))
            .WithMessage("El código debe tener de 4 a 30 letras, números o guiones, sin espacios.");
        RuleFor(x => x.Prefijo)
            .Must(p => System.Text.RegularExpressions.Regex.IsMatch(ReglasCodigoPromocional.Normalizar(p), ReglasCodigoPromocional.PatronPrefijo))
            .WithMessage("El prefijo admite hasta 12 letras o números, sin espacios ni guiones.");
        RuleFor(x => x.Descripcion).MaximumLength(200).WithMessage("La descripción admite máximo 200 caracteres.");
        RuleFor(x => x.TipoDescuento).NotNull().WithMessage("Elige el tipo de descuento.");
        RuleFor(x => x.Valor).NotNull().WithMessage("Ingresa el valor del descuento.");
        RuleFor(x => x.Valor).InclusiveBetween(1, 100).When(x => x.TipoDescuento == TipoDescuento.Porcentaje)
            .WithMessage("El porcentaje debe estar entre 1 y 100.");
        RuleFor(x => x.Valor).InclusiveBetween(1000, 10_000_000).When(x => x.TipoDescuento == TipoDescuento.ValorFijo)
            .WithMessage("El valor del descuento debe estar entre $1.000 y $10.000.000.");
        RuleFor(x => x.FechaInicio).NotNull().WithMessage("Indica desde cuándo es válido.");
        RuleFor(x => x.FechaFin).NotNull().WithMessage("Indica hasta cuándo es válido.");
        RuleFor(x => x.FechaFin).GreaterThanOrEqualTo(x => x.FechaInicio).When(x => x.FechaInicio is not null && x.FechaFin is not null)
            .WithMessage("La fecha final debe ser igual o posterior a la inicial.");
        RuleFor(x => x.Alcance).NotNull().WithMessage("Elige a quién aplica.");
        RuleFor(x => x.EmpresaIds).NotEmpty().When(x => x.Alcance == AlcanceCodigo.Empresas)
            .WithMessage("Elige al menos una empresa.");
        RuleFor(x => x.EmpresaIds!.Count).LessThanOrEqualTo(500).When(x => x.EmpresaIds is not null)
            .WithMessage("Máximo 500 empresas por código.");
        RuleForEach(x => x.Planes).NotEqual(PlanSuscripcion.Gratis).WithMessage("El plan Gratis no tiene pagos.");
        RuleFor(x => x.UsosMaximos).InclusiveBetween(1, 1_000_000).When(x => x.UsosMaximos is not null)
            .WithMessage("Los usos máximos deben ser al menos 1 (vacío = sin límite).");
        RuleFor(x => x.UsosPorEmpresa).NotNull().WithMessage("Indica cuántas veces puede usarlo cada empresa.")
            .InclusiveBetween(1, 120).WithMessage("Cada empresa puede usarlo entre 1 y 120 veces.");
        RuleFor(x => x.Estado).EstadoRegistroValido();
    }
}

public class ValidadorSolicitudValidacionCodigo : AbstractValidator<SolicitudValidacionCodigo>
{
    public ValidadorSolicitudValidacionCodigo()
    {
        RuleFor(x => x.Codigo).NotEmpty().WithMessage("Ingresa el código promocional.").MaximumLength(30).WithMessage("El código no es válido.");
        RuleFor(x => x.Plan).NotNull().WithMessage("Elige el plan.")
            .NotEqual(PlanSuscripcion.Gratis).WithMessage("El plan Gratis no requiere pago.");
        RuleFor(x => x.Periodo).NotNull().WithMessage("Elige si pagas mensual o anual.");
    }
}

/// <summary>
/// Códigos promocionales: la plataforma (SuperAdmin) los crea, edita, inactiva y ve sus usos; la empresa solo los
/// valida al pagar (<see cref="EvaluarAsync"/>). Reglas:
/// <list type="bullet">
/// <item>Activo y vigente hoy (fecha de Colombia) entre <c>fechaInicio</c> y <c>fechaFin</c>.</item>
/// <item>Alcance: TODAS, EMPRESAS (solo las elegidas) o PRIMERA_COMPRA (empresas sin pagos aplicados, sin usos de
/// códigos y que no tengan hoy un plan pagado).</item>
/// <item>Plan y periodo permitidos; usos totales y por empresa sin agotar (cuentan los usos registrados: pagos
/// aprobados o planes activados a mano con el código).</item>
/// <item>El descuento es SOLO sobre el primer mes facturado (<see cref="ReglasCodigoPromocional.Descuento"/>).</item>
/// <item>Un código con usos no se elimina (409): se inactiva.</item>
/// </list>
/// </summary>
public class ServicioCodigosPromocionales(IBaseDatos db, TimeProvider reloj)
{
    private DateOnly Hoy => Fechas.Hoy(reloj);

    // --- Plataforma --------------------------------------------------------------------------------

    public async Task<ListaPaginada<CodigoPromocionalDto>> ListarAsync(FiltrosCodigoPromocional f, Paginacion paginacion, CancellationToken ct = default)
    {
        var consulta = db.CodigosPromocionales.AsNoTracking();
        if (Texto.Opcional(f.Texto) is { } texto)
        {
            var busqueda = Busqueda.Normalizar(texto);
            var codigo = ReglasCodigoPromocional.Normalizar(texto);
            consulta = consulta.Where(c => c.Codigo.Contains(codigo) || FuncionesBd.SinTildes(c.Descripcion ?? "").Contains(busqueda));
        }
        if (Texto.Opcional(f.Estado) is { } estado)
        {
            var activo = EstadoRegistro.EsActivo(estado);
            consulta = consulta.Where(c => c.Activo == activo);
        }
        var hoy = Hoy;
        consulta = Texto.Opcional(f.Vigencia) switch
        {
            ReglasCodigoPromocional.Vigente => consulta.Where(c => c.FechaInicio <= hoy && c.FechaFin >= hoy),
            ReglasCodigoPromocional.Programado => consulta.Where(c => c.FechaInicio > hoy),
            ReglasCodigoPromocional.Vencido => consulta.Where(c => c.FechaFin < hoy),
            _ => consulta,
        };
        var total = await consulta.CountAsync(ct);
        var ids = await consulta.OrderByDescending(c => c.FechaCreacion)
            .Skip(paginacion.Omitir).Take(paginacion.TamanoValido).Select(c => c.Id).ToListAsync(ct);
        return new ListaPaginada<CodigoPromocionalDto>(await DtosAsync(ids, ct), total, paginacion.PaginaValida, paginacion.TamanoValido);
    }

    public async Task<IReadOnlyList<Opcion>> OpcionesAsync(CancellationToken ct = default) =>
        await db.CodigosPromocionales.AsNoTracking().OrderBy(c => c.Codigo).Select(c => new Opcion(c.Id, c.Codigo)).ToListAsync(ct);

    public async Task<CodigoPromocionalDto> ObtenerAsync(Guid id, CancellationToken ct = default) =>
        (await DtosAsync([id], ct)).FirstOrDefault() ?? throw ErrorNegocio.NoEncontrado("El código promocional");

    public async Task<CodigoPromocionalDto> CrearAsync(SolicitudCodigoPromocional s, CancellationToken ct = default)
    {
        var codigo = new CodigoPromocional();
        await AplicarAsync(codigo, s, ct);
        db.CodigosPromocionales.Add(codigo);
        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(codigo.Id, ct);
    }

    public async Task<CodigoPromocionalDto> ActualizarAsync(Guid id, SolicitudCodigoPromocional s, CancellationToken ct = default)
    {
        var codigo = await db.CodigosPromocionales.Include(c => c.Empresas).FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw ErrorNegocio.NoEncontrado("El código promocional");
        await AplicarAsync(codigo, s, ct);
        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(id, ct);
    }

    public async Task<string?> MotivoNoEliminarAsync(Guid id, CancellationToken ct = default)
    {
        if (!await db.CodigosPromocionales.AnyAsync(c => c.Id == id, ct)) throw ErrorNegocio.NoEncontrado("El código promocional");
        var usos = await Usos().CountAsync(u => u.IdCodigoPromocional == id, ct);
        if (usos > 0) return $"El código ya se usó ({usos}). Déjalo Inactivo para que no se pueda usar más.";
        return await db.PagosSuscripcion.IgnoreQueryFilters([FiltrosConsulta.Empresa]).AnyAsync(p => p.IdCodigoPromocional == id, ct)
            ? "Hay pagos iniciados con este código. Déjalo Inactivo para que no se pueda usar más."
            : null;
    }

    public async Task EliminarAsync(Guid id, CancellationToken ct = default)
    {
        if (await MotivoNoEliminarAsync(id, ct) is { } motivo) throw new ErrorNegocio(motivo, CodigoError.EnUso);
        var codigo = await db.CodigosPromocionales.Include(c => c.Empresas).FirstAsync(c => c.Id == id, ct);
        db.CodigosPromocionales.Remove(codigo);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<UsoCodigoDto>> UsosAsync(Guid id, CancellationToken ct = default)
    {
        if (!await db.CodigosPromocionales.AnyAsync(c => c.Id == id, ct)) throw ErrorNegocio.NoEncontrado("El código promocional");
        var usos = await Usos().AsNoTracking().Where(u => u.IdCodigoPromocional == id).OrderByDescending(u => u.FechaCreacion).Take(500).ToListAsync(ct);
        var idsEmpresa = usos.Select(u => u.IdEmpresa).Distinct().ToList();
        var idsPago = usos.Where(u => u.IdPagoSuscripcion != null).Select(u => u.IdPagoSuscripcion!.Value).ToList();
        var empresas = await db.Empresas.IgnoreQueryFilters([FiltrosConsulta.Empresa]).Where(e => idsEmpresa.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.Nombre, ct);
        var pagos = await db.PagosSuscripcion.IgnoreQueryFilters([FiltrosConsulta.Empresa]).Where(p => idsPago.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Referencia, ct);
        return usos.Select(u => new UsoCodigoDto(
            u.Id, u.IdEmpresa, empresas.GetValueOrDefault(u.IdEmpresa, ""), u.Plan, u.Periodo, u.DescuentoCentavos / 100,
            u.IdPagoSuscripcion is { } p ? pagos.GetValueOrDefault(p, "") : "", u.FechaCreacion)).ToList();
    }

    // --- Empresa (al pagar) ------------------------------------------------------------------------

    /// <summary>
    /// 422 con el motivo si el código no aplica a esa empresa, plan y periodo; si aplica, el descuento del primer mes.
    /// Se usa en modo sistema o con la empresa del token: lee los usos de todas las empresas.
    /// </summary>
    public async Task<(CodigoPromocional Codigo, ValidacionCodigoDto Resultado)> EvaluarAsync(
        string? texto, Guid idEmpresa, PlanSuscripcion plan, PeriodoPago periodo, CancellationToken ct = default)
    {
        var valor = ReglasCodigoPromocional.Normalizar(texto);
        var c = await db.CodigosPromocionales.AsNoTracking().Include(x => x.Empresas).FirstOrDefaultAsync(x => x.Codigo == valor, ct);
        if (c is null || !c.Activo) throw No("El código promocional no existe o no está activo.");
        if (plan == PlanSuscripcion.Gratis) throw No("El plan Gratis no requiere pago.");
        var hoy = Hoy;
        if (hoy < c.FechaInicio) throw No($"El código {c.Codigo} es válido desde el {Fechas.Corta(c.FechaInicio)}.");
        if (hoy > c.FechaFin) throw No($"El código {c.Codigo} venció el {Fechas.Corta(c.FechaFin)}.");
        var definicion = CatalogoPlanes.De(plan);
        var planes = ReglasCodigoPromocional.LeerPlanes(c.Planes);
        if (planes.Count > 0 && !planes.Contains(plan)) throw No($"El código {c.Codigo} no aplica para el plan {definicion.Nombre}.");
        if (c.Periodo is { } p && p != periodo)
        {
            throw No($"El código {c.Codigo} solo aplica al pago {(p == PeriodoPago.Anual ? "anual" : "mensual")}.");
        }
        switch (c.Alcance)
        {
            case AlcanceCodigo.Empresas when c.Empresas.All(e => e.IdEmpresa != idEmpresa):
                throw No($"El código {c.Codigo} no aplica para tu empresa.");
            case AlcanceCodigo.PrimeraCompra when !await EsPrimeraCompraAsync(idEmpresa, ct):
                throw No($"El código {c.Codigo} es solo para la primera compra de un plan.");
        }
        var usos = Usos().Where(u => u.IdCodigoPromocional == c.Id);
        if (c.UsosMaximos is { } maximo && await usos.CountAsync(ct) >= maximo) throw No($"El código {c.Codigo} ya alcanzó su límite de usos.");
        if (await usos.CountAsync(u => u.IdEmpresa == idEmpresa, ct) >= c.UsosPorEmpresa) throw No($"Tu empresa ya usó el código {c.Codigo}.");

        var precio = definicion.Precio(periodo);
        var descuento = ReglasCodigoPromocional.Descuento(c.TipoDescuento, c.Valor, definicion.PrecioMensual, precio);
        return (c, new ValidacionCodigoDto(c.Codigo, c.Descripcion ?? "", c.TipoDescuento, c.Valor, plan, periodo, precio, descuento, precio - descuento));
    }

    // --- Apoyo -------------------------------------------------------------------------------------

    private static ErrorNegocio No(string mensaje) => new(mensaje, CodigoError.Validacion);

    private IQueryable<UsoCodigoPromocional> Usos() => db.UsosCodigoPromocional.IgnoreQueryFilters([FiltrosConsulta.Empresa]);

    private async Task<bool> EsPrimeraCompraAsync(Guid idEmpresa, CancellationToken ct) =>
        !await db.PagosSuscripcion.IgnoreQueryFilters([FiltrosConsulta.Empresa]).AnyAsync(p => p.IdEmpresa == idEmpresa && p.FechaAplicacion != null, ct)
        && !await Usos().AnyAsync(u => u.IdEmpresa == idEmpresa, ct)
        && !await db.Suscripciones.IgnoreQueryFilters([FiltrosConsulta.Empresa]).AnyAsync(s => s.IdEmpresa == idEmpresa && s.Estado == EstadoSuscripcion.Activa, ct);

    private async Task AplicarAsync(CodigoPromocional c, SolicitudCodigoPromocional s, CancellationToken ct)
    {
        var codigo = ReglasCodigoPromocional.Normalizar(s.Codigo);
        if (codigo.Length == 0 && c.Codigo.Length > 0)
        {
            codigo = c.Codigo; // Editar sin código = se conserva.
        }
        else if (codigo.Length == 0)
        {
            // Lo genera el sistema; un choque es improbable, pero se reintenta.
            for (var intento = 0; intento < 5 && (codigo.Length == 0 || await db.CodigosPromocionales.AnyAsync(x => x.Codigo == codigo, ct)); intento++)
            {
                codigo = ReglasCodigoPromocional.Generar(s.Prefijo);
            }
        }
        if (await db.CodigosPromocionales.AnyAsync(x => x.Codigo == codigo && x.Id != c.Id, ct))
        {
            throw new ErrorNegocio($"Ya existe el código {codigo}.", CodigoError.Duplicado);
        }
        var alcance = s.Alcance!.Value;
        var empresas = alcance == AlcanceCodigo.Empresas ? (s.EmpresaIds ?? []).Distinct().ToList() : [];
        if (empresas.Count > 0)
        {
            var existentes = await db.Empresas.IgnoreQueryFilters([FiltrosConsulta.Empresa]).CountAsync(e => empresas.Contains(e.Id), ct);
            if (existentes != empresas.Count) throw ErrorNegocio.NoEncontrado("Alguna de las empresas elegidas");
        }
        c.Codigo = codigo;
        c.Descripcion = Texto.Opcional(s.Descripcion);
        c.TipoDescuento = s.TipoDescuento!.Value;
        c.Valor = s.Valor!.Value;
        c.FechaInicio = s.FechaInicio!.Value;
        c.FechaFin = s.FechaFin!.Value;
        c.Alcance = alcance;
        c.Planes = ReglasCodigoPromocional.EscribirPlanes(s.Planes);
        c.Periodo = s.Periodo;
        c.UsosMaximos = s.UsosMaximos;
        c.UsosPorEmpresa = s.UsosPorEmpresa!.Value;
        c.Activo = EstadoRegistro.EsActivo(s.Estado);
        c.Empresas.RemoveAll(e => !empresas.Contains(e.IdEmpresa));
        foreach (var id in empresas.Where(id => c.Empresas.All(e => e.IdEmpresa != id)))
        {
            c.Empresas.Add(new CodigoPromocionalEmpresa { IdCodigoPromocional = c.Id, IdEmpresa = id });
        }
    }

    private async Task<IReadOnlyList<CodigoPromocionalDto>> DtosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var codigos = await db.CodigosPromocionales.AsNoTracking().Include(c => c.Empresas).Where(c => ids.Contains(c.Id)).ToListAsync(ct);
        var usos = await Usos().Where(u => ids.Contains(u.IdCodigoPromocional)).GroupBy(u => u.IdCodigoPromocional)
            .Select(g => new { g.Key, Total = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Total, ct);
        var idsEmpresa = codigos.SelectMany(c => c.Empresas.Select(e => e.IdEmpresa)).Distinct().ToList();
        var nombres = await db.Empresas.IgnoreQueryFilters([FiltrosConsulta.Empresa]).Where(e => idsEmpresa.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.Nombre, ct);
        var hoy = Hoy;
        return ids.Select(id => codigos.FirstOrDefault(c => c.Id == id)).OfType<CodigoPromocional>().Select(c => new CodigoPromocionalDto(
            c.Id, c.Codigo, c.Descripcion ?? "", c.TipoDescuento, c.Valor, c.FechaInicio, c.FechaFin, c.Alcance,
            ReglasCodigoPromocional.LeerPlanes(c.Planes), c.Periodo is { } p ? TextoEnum.Escribir(p) : "", c.UsosMaximos, c.UsosPorEmpresa,
            EstadoRegistro.Texto(c.Activo), c.Empresas.Select(e => e.IdEmpresa).ToList(),
            c.Empresas.Select(e => new Opcion(e.IdEmpresa, nombres.GetValueOrDefault(e.IdEmpresa, ""))).OrderBy(o => o.Nombre).ToList(),
            usos.GetValueOrDefault(c.Id), ReglasCodigoPromocional.Vigencia(c.FechaInicio, c.FechaFin, hoy), c.FechaCreacion)).ToList();
    }
}
