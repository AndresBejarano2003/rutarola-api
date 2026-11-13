using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Domain.Enums;
using RutaRola.Domain.Suscripciones;

namespace RutaRola.Application.Suscripciones;

/// <summary>Parámetro global de la plataforma (lo edita el SuperAdmin en Plataforma › Ajustes).</summary>
public record ParametroPlataformaDto(string Clave, string Nombre, string Descripcion, string Valor, int Minimo, int Maximo);

public record SolicitudParametroPlataforma(string? Valor);

/// <summary>Condiciones públicas del registro (<c>GET /suscripcion/condiciones</c>).</summary>
public record CondicionesRegistroDto(int DiasPrueba, PlanSuscripcion PlanPrueba, string NombrePlanPrueba);

internal record DefinicionParametroPlataforma(string Clave, string Nombre, string Descripcion, int PorDefecto, int Minimo, int Maximo);

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
/// </list>
/// </summary>
public class ServicioParametrosPlataforma(IBaseDatos db)
{
    public const string DiasPrueba = "suscripcion.dias_prueba";

    private static readonly IReadOnlyList<DefinicionParametroPlataforma> Catalogo =
    [
        new(DiasPrueba, "Días de prueba gratuita",
            "Días del plan Profesional que recibe una empresa al registrarse. Solo aplica a las empresas nuevas: las que ya tienen su prueba conservan los días que recibieron.",
            15, 1, 90),
    ];

    /// <summary>Días de prueba vigentes hoy (para crear una suscripción nueva).</summary>
    public static async Task<int> DiasPruebaAsync(IBaseDatos db, CancellationToken ct)
    {
        var definicion = Catalogo.First(p => p.Clave == DiasPrueba);
        var valor = await db.ParametrosPlataforma.AsNoTracking().Where(p => p.Clave == DiasPrueba).Select(p => p.Valor).FirstOrDefaultAsync(ct);
        return int.TryParse(valor, out var dias) && dias >= definicion.Minimo && dias <= definicion.Maximo ? dias : definicion.PorDefecto;
    }

    public async Task<CondicionesRegistroDto> CondicionesAsync(CancellationToken ct = default)
    {
        var plan = CatalogoPlanes.De(CatalogoPlanes.PlanPrueba);
        return new CondicionesRegistroDto(await DiasPruebaAsync(db, ct), plan.Plan, plan.Nombre);
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
        new(p.Clave, p.Nombre, p.Descripcion, valor ?? p.PorDefecto.ToString(), p.Minimo, p.Maximo);
}
