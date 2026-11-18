using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Domain.Enums;
using RutaRola.Domain.Suscripciones;

namespace RutaRola.Application.Suscripciones;

/// <summary>Parámetro global de la plataforma (lo edita el SuperAdmin en Plataforma › Ajustes).</summary>
/// <summary><c>tipo</c> DIAS o PESOS; <c>grupo</c> MEMBRESIAS (prueba y avisos) o PRECIOS (valor mensual de cada plan pagado).</summary>
public record ParametroPlataformaDto(string Clave, string Nombre, string Descripcion, string Valor, int Minimo, int Maximo, string Tipo = "DIAS", string Grupo = "MEMBRESIAS");

public record SolicitudParametroPlataforma(string? Valor);

/// <summary>Condiciones públicas del registro (<c>GET /suscripcion/condiciones</c>).</summary>
/// <summary><c>diasAvisoVencimiento</c>: desde cuántos días antes del fin del plan se avisa (el aviso del shell lo usa).</summary>
public record CondicionesRegistroDto(int DiasPrueba, PlanSuscripcion PlanPrueba, string NombrePlanPrueba, int DiasAvisoVencimiento = 5);

internal record DefinicionParametroPlataforma(string Clave, string Nombre, string Descripcion, int PorDefecto, int Minimo, int Maximo, string Tipo = "DIAS", string Grupo = "MEMBRESIAS");

public class ValidadorSolicitudParametroPlataforma : AbstractValidator<SolicitudParametroPlataforma>
{
    public ValidadorSolicitudParametroPlataforma() =>
        RuleFor(x => x.Valor).NotEmpty().WithMessage("Ingresa el valor.");
}

/// <summary>
/// Parámetros globales (tabla <c>parametros_plataforma</c>). Sin fila se usa el valor por defecto.
/// <list type="bullet">
/// <item><c>suscripcion.dias_prueba</c> (1–90, por defecto 15): días de prueba del plan Profesional para las empresas que
/// se registren DESDE ese momento. Cada suscripción guarda sus días al crearse, así que cambiarlo nunca altera una
/// prueba ya otorgada.</item>
/// <item><c>suscripcion.dias_aviso_vencimiento</c> (1–60, por defecto 5): desde cuántos días antes del fin del plan o de la
/// prueba se avisa a todos los administradores de la empresa (campana y correo).</item>
/// <item><c>plan.{esencial|profesional|empresarial}.precio_mensual</c> (pesos con IVA): valor mensual de cada plan pagado; el
/// anual es <see cref="CatalogoPlanes.MesesPagadosPorAnio"/> mensualidades. Aplica a los pagos que se hagan desde ese momento.</item>
/// </list>
/// </summary>
public class ServicioParametrosPlataforma(IBaseDatos db)
{
    public const string DiasPrueba = "suscripcion.dias_prueba";
    public const string DiasAvisoVencimiento = "suscripcion.dias_aviso_vencimiento";
    public const int DiasAvisoVencimientoPorDefecto = 5;

    public static string ClavePrecio(PlanSuscripcion plan) => $"plan.{plan.ToString().ToLowerInvariant()}.precio_mensual";

    private const int PrecioMinimo = 1_000;
    private const int PrecioMaximo = 100_000_000;

    private static readonly IReadOnlyList<DefinicionParametroPlataforma> Catalogo =
    [
        new(DiasPrueba, "Días de prueba gratuita",
            "Días del plan Profesional que recibe una empresa al registrarse. Solo aplica a las empresas nuevas: las que ya tienen su prueba conservan los días que recibieron.",
            15, 1, 90),
        new(DiasAvisoVencimiento, "Días de aviso antes del vencimiento",
            "Desde cuántos días antes de que termine el plan pagado o la prueba se avisa a todos los administradores de la empresa (campana y correo). Si no paga, el día siguiente al vencimiento queda en el plan Gratis.",
            DiasAvisoVencimientoPorDefecto, 1, 60),
        .. CatalogoPlanes.Base.Where(p => p.PrecioMensual > 0).Select(p => new DefinicionParametroPlataforma(
            ClavePrecio(p.Plan), $"Plan {p.Nombre}: valor mensual",
            $"Pesos con IVA. El anual equivale a {CatalogoPlanes.MesesPagadosPorAnio} mensualidades. Aplica a los pagos que se hagan desde ahora; los planes ya pagados no cambian.",
            (int)p.PrecioMensual, PrecioMinimo, PrecioMaximo, "PESOS", "PRECIOS")),
    ];

    private static async Task<int> EnteroAsync(IBaseDatos db, string clave, CancellationToken ct)
    {
        var definicion = Catalogo.First(p => p.Clave == clave);
        var valor = await db.ParametrosPlataforma.AsNoTracking().Where(p => p.Clave == clave).Select(p => p.Valor).FirstOrDefaultAsync(ct);
        return int.TryParse(valor, out var n) && n >= definicion.Minimo && n <= definicion.Maximo ? n : definicion.PorDefecto;
    }

    /// <summary>Días antes del fin del plan o de la prueba en que se avisa a los administradores.</summary>
    public static Task<int> DiasAvisoVencimientoAsync(IBaseDatos db, CancellationToken ct) => EnteroAsync(db, DiasAvisoVencimiento, ct);

    /// <summary>Los planes con el valor mensual que fijó la plataforma (sin fila = el del catálogo).</summary>
    public static async Task<IReadOnlyList<DefinicionPlan>> PlanesAsync(IBaseDatos db, CancellationToken ct)
    {
        var claves = CatalogoPlanes.Base.Select(p => ClavePrecio(p.Plan)).ToList();
        var guardados = await db.ParametrosPlataforma.AsNoTracking().Where(p => claves.Contains(p.Clave)).ToDictionaryAsync(p => p.Clave, p => p.Valor, ct);
        return CatalogoPlanes.Base.Select(p =>
            p.PrecioMensual > 0 && long.TryParse(guardados.GetValueOrDefault(ClavePrecio(p.Plan)), out var precio) && precio is >= PrecioMinimo and <= PrecioMaximo
                ? p with { PrecioMensual = precio }
                : p).ToList();
    }

    public static async Task<DefinicionPlan> PlanAsync(IBaseDatos db, PlanSuscripcion plan, CancellationToken ct) =>
        (await PlanesAsync(db, ct)).First(p => p.Plan == plan);

    /// <summary>Días de prueba vigentes hoy (para crear una suscripción nueva).</summary>
    public static Task<int> DiasPruebaAsync(IBaseDatos db, CancellationToken ct) => EnteroAsync(db, DiasPrueba, ct);

    public async Task<CondicionesRegistroDto> CondicionesAsync(CancellationToken ct = default)
    {
        var plan = CatalogoPlanes.De(CatalogoPlanes.PlanPrueba);
        return new CondicionesRegistroDto(await DiasPruebaAsync(db, ct), plan.Plan, plan.Nombre, await DiasAvisoVencimientoAsync(db, ct));
    }

    public async Task<IReadOnlyList<ParametroPlataformaDto>> ListarAsync(CancellationToken ct = default)
    {
        var guardados = await db.ParametrosPlataforma.AsNoTracking().ToDictionaryAsync(p => p.Clave, p => p.Valor, ct);
        return Catalogo.Select(p => Dto(p, guardados.GetValueOrDefault(p.Clave))).ToList();
    }

    public async Task<ParametroPlataformaDto> ActualizarAsync(string clave, SolicitudParametroPlataforma s, CancellationToken ct = default)
    {
        var definicion = Catalogo.FirstOrDefault(p => p.Clave == clave) ?? throw ErrorNegocio.NoEncontrado("El parámetro");
        if (!int.TryParse(s.Valor, out var numero) || numero < definicion.Minimo || numero > definicion.Maximo)
        {
            throw new ValidationException([new FluentValidation.Results.ValidationFailure(
                "valor", $"El valor debe ser un número entre {definicion.Minimo} y {definicion.Maximo}.")]);
        }
        db.UsarModoSistema();
        var registro = await db.ParametrosPlataforma.FirstOrDefaultAsync(p => p.Clave == clave, ct);
        if (registro is null)
        {
            registro = new ParametroPlataforma { Clave = clave };
            db.ParametrosPlataforma.Add(registro);
        }
        registro.Valor = numero.ToString();
        await db.SaveChangesAsync(ct);
        return Dto(definicion, registro.Valor);
    }

    private static ParametroPlataformaDto Dto(DefinicionParametroPlataforma p, string? valor) =>
        new(p.Clave, p.Nombre, p.Descripcion, valor ?? p.PorDefecto.ToString(), p.Minimo, p.Maximo, p.Tipo, p.Grupo);
}
