namespace RutaRola.Application.Configuracion;

/// <summary>Parámetro de <c>configuracion_sistema</c>: solo se aceptan las claves de este catálogo.</summary>
public record ParametroConfiguracion(string Clave, string Descripcion, string ValorPorDefecto, int Minimo, int Maximo);

public static class CatalogoConfiguracion
{
    public const string DiaLimitePago = "finanzas.dia_limite_pago";
    public const string DiasAlertaVencimiento = "alertas.dias_vencimiento";

    public static readonly IReadOnlyList<ParametroConfiguracion> Parametros =
    [
        new(DiaLimitePago, "Día del mes en que vence la cuota cuando la factura no tiene programación (por defecto 5).", "5", 1, 31),
        new(DiasAlertaVencimiento, "Días antes del vencimiento de documentos y licencias en que se avisa (por defecto 30).", "30", 1, 120),
    ];

    public static ParametroConfiguracion? Buscar(string clave) => Parametros.FirstOrDefault(p => p.Clave == clave);
}
