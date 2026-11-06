namespace RutaRola.Domain.Comun;

/// <summary>Registro con id UUID v7 (ordenable) y fechas de auditoría en UTC.</summary>
public abstract class Entidad
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaActualizacion { get; set; }
}

/// <summary>
/// Registro que pertenece a una empresa de transporte. El DbContext filtra por la empresa del token
/// (global query filter) y asigna <see cref="IdEmpresa"/> al crear: nunca se toma del body.
/// </summary>
public interface IEntidadEmpresa
{
    Guid IdEmpresa { get; set; }
}

public abstract class EntidadEmpresa : Entidad, IEntidadEmpresa
{
    public Guid IdEmpresa { get; set; }
}

/// <summary>Datos de identificación comunes a estudiantes, acudientes y empleados (igual que el frontend).</summary>
public abstract class Persona : EntidadEmpresa
{
    /// <summary>RC, TI, CC, CE, PPT, PA.</summary>
    public string TipoDocumento { get; set; } = "";
    public string NumeroDocumento { get; set; } = "";
    public string PrimerNombre { get; set; } = "";
    public string? SegundoNombre { get; set; }
    public string PrimerApellido { get; set; } = "";
    public string? SegundoApellido { get; set; }

    public string NombreCompleto =>
        string.Join(' ', new[] { PrimerNombre, SegundoNombre, PrimerApellido, SegundoApellido }.Where(p => !string.IsNullOrWhiteSpace(p)));
}
