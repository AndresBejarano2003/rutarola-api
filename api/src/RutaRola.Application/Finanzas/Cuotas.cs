using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Domain.Enums;
using RutaRola.Domain.Finanzas;

namespace RutaRola.Application.Finanzas;

/// <summary>Valor de recibos vigentes aplicado a un mes de una factura.</summary>
public record PagoMes(DateOnly Periodo, decimal Valor);

/// <summary>Mensualidad de una factura: valor del mes, lo pagado (recibos completos y abonos) y el saldo.</summary>
public record Cuota(DateOnly Periodo, decimal Valor, decimal Pagado)
{
    public decimal Saldo => Math.Max(0, Valor - Pagado);
    public bool Pagada => Saldo <= 0;
    /// <summary>Tiene un pago parcial (abono) pero sigue con saldo.</summary>
    public bool Abonada => Pagado > 0 && Saldo > 0;
}

/// <summary>Cómo aplica un recibo su valor: a los meses elegidos o al saldo de la factura (los más antiguos primero).</summary>
public enum AplicacionRecibo { Meses, Saldo }

/// <summary>
/// Reglas de las cuotas (iguales a <c>finanzas/cuotas.ts</c>). Una factura vigente cobra cada mes de
/// <c>periodoInicio</c> a <c>periodoFin</c>, salvo desde <c>periodoRetiro</c> (retiro del estudiante o del acudiente).
/// Un mes está pagado cuando la suma de lo aplicado por recibos vigentes cubre el valor mensual; si no, el saldo
/// sigue pendiente (abono parcial). Un recibo reparte su valor en orden cronológico: primero completa los meses
/// más antiguos y lo que sobra queda como abono del siguiente.
/// </summary>
public static class ReglasCuotas
{
    /// <summary>Último mes que se cobra (null si el retiro es desde el primer mes: no cobra ninguno).</summary>
    public static DateOnly? UltimoMesCobrable(DateOnly inicio, DateOnly fin, DateOnly? retiro)
    {
        var ultimo = retiro is { } r && r <= fin ? r.AddMonths(-1) : fin;
        return ultimo < inicio ? null : ultimo;
    }

    public static IReadOnlyList<DateOnly> MesesCobrables(Factura f) =>
        UltimoMesCobrable(f.PeriodoInicio, f.PeriodoFin, f.PeriodoRetiro) is { } ultimo ? Periodos.Entre(f.PeriodoInicio, ultimo).ToList() : [];

    public static IReadOnlyList<Cuota> Cuotas(Factura f, IEnumerable<PagoMes> pagos)
    {
        var pagado = pagos.GroupBy(p => p.Periodo).ToDictionary(g => g.Key, g => g.Sum(p => p.Valor));
        return MesesCobrables(f).Select(p => new Cuota(p, f.ValorMensual, pagado.GetValueOrDefault(p))).ToList();
    }

    /// <summary>
    /// Reparte <paramref name="valor"/> sobre los meses con saldo (todos o solo <paramref name="meses"/>), del más antiguo al
    /// más reciente. 422 si un mes elegido no se cobra o ya está pagado, o si el valor supera el saldo disponible.
    /// </summary>
    public static IReadOnlyList<PagoMes> Distribuir(
        IReadOnlyList<Cuota> cuotas, decimal valor, AplicacionRecibo aplicacion, IReadOnlyCollection<DateOnly>? meses, string numeroFactura)
    {
        IEnumerable<Cuota> destino;
        if (aplicacion == AplicacionRecibo.Meses)
        {
            if (meses is null || meses.Count == 0)
            {
                throw new ErrorNegocio("Elige al menos un mes a pagar.", CodigoError.Validacion);
            }
            var porMes = cuotas.ToDictionary(c => c.Periodo);
            foreach (var mes in meses.Order())
            {
                if (!porMes.TryGetValue(mes, out var cuota))
                {
                    throw new ErrorNegocio($"{Periodos.Nombre(mes, true)} no se cobra en la factura {numeroFactura}.", CodigoError.Validacion);
                }
                if (cuota.Pagada)
                {
                    throw new ErrorNegocio($"{Periodos.Nombre(mes, true)} de la factura {numeroFactura} ya está pagado.", CodigoError.Duplicado);
                }
            }
            destino = meses.Distinct().Order().Select(m => porMes[m]);
        }
        else
        {
            destino = cuotas.Where(c => !c.Pagada);
        }

        var lista = destino.ToList();
        var disponible = lista.Sum(c => c.Saldo);
        if (disponible <= 0)
        {
            throw new ErrorNegocio($"La factura {numeroFactura} no tiene saldo pendiente.", CodigoError.Validacion);
        }
        if (valor > disponible)
        {
            throw new ErrorNegocio(
                aplicacion == AplicacionRecibo.Meses
                    ? $"El valor supera el saldo de los meses elegidos ({Cobro.Pesos(disponible)})."
                    : $"El valor supera el saldo de la factura {numeroFactura} ({Cobro.Pesos(disponible)}).",
                CodigoError.Validacion);
        }

        var resto = valor;
        var lineas = new List<PagoMes>();
        foreach (var cuota in lista)
        {
            if (resto <= 0) break;
            var aplicado = Math.Min(resto, cuota.Saldo);
            lineas.Add(new PagoMes(cuota.Periodo, aplicado));
            resto -= aplicado;
        }
        return lineas;
    }

    /// <summary>Lo aplicado por recibos vigentes a cada mes de las facturas indicadas (opcionalmente sin un recibo).</summary>
    public static async Task<Dictionary<Guid, List<PagoMes>>> PagosAsync(
        IBaseDatos db, IReadOnlyCollection<Guid> idsFacturas, CancellationToken ct, Guid? sinRecibo = null)
    {
        var filas = await db.PagosDetalle.AsNoTracking()
            .Where(d => d.Pago.Estado == EstadoPago.Vigente && d.Pago.IdFactura != null && idsFacturas.Contains(d.Pago.IdFactura.Value)
                && d.IdPago != sinRecibo)
            .Select(d => new { IdFactura = d.Pago.IdFactura!.Value, d.Periodo, d.Valor })
            .ToListAsync(ct);
        return filas.GroupBy(f => f.IdFactura).ToDictionary(g => g.Key, g => g.Select(f => new PagoMes(f.Periodo, f.Valor)).ToList());
    }
}
