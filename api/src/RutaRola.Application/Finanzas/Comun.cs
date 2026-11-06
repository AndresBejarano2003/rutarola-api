using System.Globalization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;

namespace RutaRola.Application.Finanzas;

/// <summary>Periodos mensuales <c>YYYY-MM</c> (en la BD, el día 1 del mes), como <c>shared/fechas</c> del frontend.</summary>
public static class Periodos
{
    public static readonly CultureInfo Colombia = CultureInfo.GetCultureInfo("es-CO");

    public static bool EsValido(string? periodo) =>
        periodo is { Length: 7 } && DateOnly.TryParseExact(periodo + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    public static DateOnly Leer(string periodo) => DateOnly.ParseExact(periodo + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string Texto(DateOnly periodo) => periodo.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    public static string Texto(DateOnly? periodo) => periodo is { } p ? Texto(p) : "";

    public static DateOnly De(DateOnly fecha) => new(fecha.Year, fecha.Month, 1);

    /// <summary>De <paramref name="desde"/> a <paramref name="hasta"/>, ambos incluidos.</summary>
    public static IEnumerable<DateOnly> Entre(DateOnly desde, DateOnly hasta)
    {
        for (var p = De(desde); p <= hasta; p = p.AddMonths(1))
        {
            yield return p;
        }
    }

    /// <summary><c>noviembre de 2026</c> (o <c>nov. 2026</c>), igual que <c>nombrePeriodo</c>.</summary>
    public static string Nombre(DateOnly periodo, bool largo = false) =>
        largo ? periodo.ToString("MMMM 'de' yyyy", Colombia) : periodo.ToString("MMM yyyy", Colombia);

    /// <summary>Día indicado del mes; si el mes es más corto, su último día (<c>diaDelPeriodo</c>).</summary>
    public static DateOnly Dia(DateOnly periodo, int dia) =>
        new(periodo.Year, periodo.Month, Math.Min(dia, DateTime.DaysInMonth(periodo.Year, periodo.Month)));

    public static IRuleBuilderOptions<T, string?> PeriodoValido<T>(this IRuleBuilder<T, string?> regla, string campo) =>
        regla.NotEmpty().WithMessage($"Selecciona el {campo}.")
            .Must(p => p is null || EsValido(p)).WithMessage($"El {campo} debe tener el formato AAAA-MM.");
}

/// <summary>Reglas de cobro compartidas por reportes, tablero y portal (iguales a <c>finanzas/reportes/reportes.ts</c>).</summary>
public static class Cobro
{
    /// <summary>Día límite de pago cuando la factura no tiene programación.</summary>
    public const int DiaLimitePredeterminado = 5;

    /// <param name="predeterminado">Parámetro <c>finanzas.dia_limite_pago</c> de la empresa, para facturas sin programación.</param>
    public static DateOnly FechaLimite(DateOnly periodo, int? diaLimitePago, int predeterminado = DiaLimitePredeterminado) =>
        Periodos.Dia(periodo, diaLimitePago ?? predeterminado);

    /// <summary><c>$ 420.000</c>, como el formato de moneda del frontend.</summary>
    public static string Pesos(decimal valor) => "$ " + Math.Round(valor).ToString("N0", Periodos.Colombia);
}

/// <summary>Consecutivos por empresa generados por el servidor (<c>FAC-0001</c>, <c>REC-0001</c>, <c>COT-0001</c>).</summary>
public static class Consecutivos
{
    /// <summary>
    /// Siguiente número tras el mayor existente. Dos creaciones simultáneas chocan con el índice único
    /// (empresa, número) y la segunda recibe 409 (se reintenta).
    /// </summary>
    public static async Task<string> SiguienteAsync(IQueryable<string> numeros, string prefijo, CancellationToken ct)
    {
        var inicio = prefijo + "-";
        var existentes = await numeros.Where(n => n.StartsWith(inicio)).ToListAsync(ct);
        var mayor = existentes.Select(n => int.TryParse(n[inicio.Length..], out var v) ? v : 0).DefaultIfEmpty(0).Max();
        return $"{inicio}{mayor + 1:0000}";
    }
}
