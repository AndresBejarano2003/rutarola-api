namespace RutaRola.Application.Configuracion;

/// <summary>Parámetro de <c>configuracion_sistema</c>: solo se aceptan las claves de este catálogo.</summary>
public record ParametroConfiguracion(string Clave, string Descripcion, string ValorPorDefecto, int Minimo, int Maximo);

public static class CatalogoConfiguracion
{
    public const string DiaLimitePago = "finanzas.dia_limite_pago";
    public const string DiasAlertaVencimiento = "alertas.dias_vencimiento";
    /// <summary>1 = los acudientes ven en el portal la ubicación en vivo del vehículo; 0 = solo el estado y la hora de inicio.</summary>
    public const string AcudientesVenUbicacion = "gps.acudientes_ven_ubicacion";

    public static readonly IReadOnlyList<ParametroConfiguracion> Parametros =
    [
        new(DiaLimitePago, "Día del mes en que vence la cuota cuando la factura no tiene programación (por defecto 5).", "5", 1, 31),
        new(DiasAlertaVencimiento, "Días antes del vencimiento de documentos y licencias en que se avisa (por defecto 30).", "30", 1, 120),
        new(AcudientesVenUbicacion, "Los acudientes ven en su portal la ubicación en vivo del vehículo mientras su ruta va en camino. Si se desactiva, solo ven el estado del estudiante y la hora en que inició el recorrido; el administrador sigue viendo el monitoreo completo.", "1", 0, 1),
    ];

    public static ParametroConfiguracion? Buscar(string clave) => Parametros.FirstOrDefault(p => p.Clave == clave);
}
