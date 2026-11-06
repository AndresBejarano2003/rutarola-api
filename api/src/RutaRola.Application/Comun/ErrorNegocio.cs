namespace RutaRola.Application.Comun;

/// <summary>Tipos de error de negocio y el código HTTP con el que se responden (ProblemDetails + <c>mensaje</c>).</summary>
public enum CodigoError
{
    /// <summary>404</summary>
    NoEncontrado,
    /// <summary>409: ya existe un registro con esos datos.</summary>
    Duplicado,
    /// <summary>409: el registro está en uso y no se puede eliminar.</summary>
    EnUso,
    /// <summary>409: conflicto de estado o concurrencia.</summary>
    Conflicto,
    /// <summary>422: regla de negocio no cumplida.</summary>
    Validacion,
    /// <summary>401</summary>
    NoAutenticado,
    /// <summary>403</summary>
    Prohibido,
}

/// <summary>Error con un mensaje listo para mostrar al usuario (lo lee <c>mensajeDeError()</c> del frontend).</summary>
public class ErrorNegocio(string mensaje, CodigoError codigo) : Exception(mensaje)
{
    public CodigoError Codigo { get; } = codigo;

    public static ErrorNegocio NoEncontrado(string entidad = "El registro") =>
        new($"{entidad} no existe o fue {(entidad.StartsWith("La ") ? "eliminada" : "eliminado")}.", CodigoError.NoEncontrado);
}
