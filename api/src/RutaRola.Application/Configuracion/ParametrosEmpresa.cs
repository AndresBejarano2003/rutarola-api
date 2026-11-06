using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;

namespace RutaRola.Application.Configuracion;

/// <summary>
/// Parámetros de <c>configuracion_sistema</c> de la empresa del contexto, leídos una vez por petición.
/// Un valor ausente o fuera de rango usa el del catálogo (<see cref="CatalogoConfiguracion"/>).
/// </summary>
public class ParametrosEmpresa(IBaseDatos db)
{
    private Dictionary<string, string>? valores;

    /// <summary>Día del mes en que vence la cuota de una factura sin programación de cobro.</summary>
    public Task<int> DiaLimitePagoAsync(CancellationToken ct = default) => EnteroAsync(CatalogoConfiguracion.DiaLimitePago, ct);

    /// <summary>Días antes del vencimiento de documentos y licencias en que pasan a "por vencer".</summary>
    public Task<int> DiasAlertaVencimientoAsync(CancellationToken ct = default) => EnteroAsync(CatalogoConfiguracion.DiasAlertaVencimiento, ct);

    private async Task<int> EnteroAsync(string clave, CancellationToken ct)
    {
        var parametro = CatalogoConfiguracion.Buscar(clave)!;
        valores ??= await db.ConfiguracionSistema.AsNoTracking().ToDictionaryAsync(c => c.Clave, c => c.Valor, ct);
        return valores.TryGetValue(clave, out var texto)
               && int.TryParse(texto, NumberStyles.Integer, CultureInfo.InvariantCulture, out var valor)
               && valor >= parametro.Minimo && valor <= parametro.Maximo
            ? valor
            : int.Parse(parametro.ValorPorDefecto, CultureInfo.InvariantCulture);
    }
}
