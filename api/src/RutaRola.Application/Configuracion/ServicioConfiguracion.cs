using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Domain.Seguridad;

namespace RutaRola.Application.Configuracion;

public record ParametroDto(string Clave, string Descripcion, string Valor, int Minimo, int Maximo);

public record SolicitudParametro(string? Valor);

/// <summary>Configuración de la empresa del token (cada empresa tiene la suya).</summary>
public class ServicioConfiguracion(IBaseDatos db)
{
    public async Task<IReadOnlyList<ParametroDto>> ListarAsync(CancellationToken ct = default)
    {
        var guardados = await db.ConfiguracionSistema.ToDictionaryAsync(c => c.Clave, c => c.Valor, ct);
        return CatalogoConfiguracion.Parametros
            .Select(p => new ParametroDto(p.Clave, p.Descripcion, guardados.GetValueOrDefault(p.Clave, p.ValorPorDefecto), p.Minimo, p.Maximo))
            .ToList();
    }

    public async Task<ParametroDto> ActualizarAsync(string clave, SolicitudParametro solicitud, CancellationToken ct = default)
    {
        var parametro = CatalogoConfiguracion.Buscar(clave) ?? throw ErrorNegocio.NoEncontrado("El parámetro");
        if (!int.TryParse(solicitud.Valor, out var numero) || numero < parametro.Minimo || numero > parametro.Maximo)
        {
            throw new ValidationException([new FluentValidation.Results.ValidationFailure(
                "valor", $"El valor debe ser un número entre {parametro.Minimo} y {parametro.Maximo}.")]);
        }

        var registro = await db.ConfiguracionSistema.FirstOrDefaultAsync(c => c.Clave == clave, ct);
        if (registro is null)
        {
            registro = new ConfiguracionSistema { Clave = clave };
            db.ConfiguracionSistema.Add(registro);
        }
        registro.Valor = numero.ToString();
        await db.SaveChangesAsync(ct);
        return new ParametroDto(parametro.Clave, parametro.Descripcion, registro.Valor, parametro.Minimo, parametro.Maximo);
    }
}
