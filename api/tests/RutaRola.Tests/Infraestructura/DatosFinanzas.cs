namespace RutaRola.Tests.Infraestructura;

/// <summary>Solicitudes de Finanzas como las envía el frontend.</summary>
public static class DatosFinanzas
{
    public static string Periodo(int meses = 0) => DatosOperacion.Hoy.AddMonths(meses).ToString("yyyy-MM");

    public static Dictionary<string, object?> Factura(Guid acudienteId, Guid estudianteId, string? inicio = null, string? fin = null,
        decimal valor = 420000, string estado = "ACTIVO") => new()
    {
        ["numero"] = "",
        ["fechaEmision"] = DatosOperacion.Hoy.ToString("yyyy-MM-dd"),
        ["acudienteId"] = acudienteId,
        ["estudianteId"] = estudianteId,
        ["nombreAcudiente"] = "ignorado",
        ["nombreEstudiante"] = "ignorado",
        ["valorContratacion"] = valor,
        ["periodoInicio"] = inicio ?? Periodo(-3),
        ["periodoFin"] = fin ?? Periodo(3),
        ["concepto"] = "Transporte escolar",
        ["estado"] = estado,
        ["observaciones"] = "",
    };

    public static Dictionary<string, object?> Recibo(Guid facturaId, string periodo, decimal valor = 420000, string medio = "NEQUI",
        string estado = "ACTIVO", string? fechaPago = null) => new()
    {
        ["numero"] = "",
        ["facturaId"] = facturaId,
        ["periodo"] = periodo,
        ["fechaPago"] = fechaPago ?? DatosOperacion.Hoy.ToString("yyyy-MM-dd"),
        ["valor"] = valor,
        ["medioPago"] = medio,
        ["referencia"] = "48210137",
        ["estado"] = estado,
        ["observaciones"] = "",
    };

    public static object Plantilla(string estado, string cuerpo = "Hola {acudiente}, vence {fecha_limite}.", string asunto = "", object[]? adjuntos = null, Guid? id = null) =>
        new { id = (id ?? Guid.NewGuid()).ToString(), asunto, cuerpo, adjuntos = adjuntos ?? [], estado };

    public static Dictionary<string, object?> Programacion(Guid facturaId, object[]? whatsapp = null, object[]? correos = null,
        bool conWhatsapp = true, bool conEmail = false, string estado = "ACTIVO", int dia = 10) => new()
    {
        ["nombre"] = "Mensualidad Juan",
        ["facturaId"] = facturaId,
        ["diaLimitePago"] = dia,
        ["diasPrevios"] = 3,
        ["whatsapp"] = conWhatsapp,
        ["mensajesWhatsapp"] = whatsapp ?? [Plantilla("EN_USO")],
        ["email"] = conEmail,
        ["correos"] = correos ?? [],
        ["estado"] = estado,
    };

    public static object Adjunto(string nombre = "tarifas.pdf") =>
        new { nombre, tipo = "application/pdf", tamano = 8, url = "data:application/pdf;base64," + Convert.ToBase64String("%PDF-1.4"u8.ToArray()) };
}
