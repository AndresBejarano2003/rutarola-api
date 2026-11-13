using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Domain.Seguridad;

namespace RutaRola.Application.Configuracion;

/// <summary>
/// Encabezado y pie de los PDF de la empresa (facturas, recibos, cotizaciones), como lo usa
/// <c>shared/documentos-pdf/</c>. <c>personalizado</c> false = aún no se ha guardado: los campos traen los datos de
/// contacto de la empresa. <c>logo</c>: data URL o <c>""</c>.
/// </summary>
public record FormatoDocumentosDto(
    string Logo, string NombreComercial, string Eslogan, string Nit, string Direccion, string Ciudad, string Telefono, string Correo,
    string SitioWeb, string ColorPrincipal, string PiePagina, string NotaFactura, string NotaRecibo, string NotaCotizacion, bool Personalizado);

public record SolicitudFormatoDocumentos(
    string? Logo, string? NombreComercial, string? Eslogan, string? Nit, string? Direccion, string? Ciudad, string? Telefono, string? Correo,
    string? SitioWeb, string? ColorPrincipal, string? PiePagina, string? NotaFactura, string? NotaRecibo, string? NotaCotizacion);

public class ValidadorSolicitudFormatoDocumentos : AbstractValidator<SolicitudFormatoDocumentos>
{
    /// <summary>Unos 300 KB de imagen en base64 (el frontend lo reduce a 600 px de ancho).</summary>
    public const int MaximoLogo = 400_000;

    private static readonly string[] PrefijosLogo =
        ["data:image/png;base64,", "data:image/jpeg;base64,", "data:image/webp;base64,", "data:image/svg+xml;base64,"];

    public ValidadorSolicitudFormatoDocumentos()
    {
        RuleFor(x => x.Logo).Must(l => string.IsNullOrEmpty(l) || (l.Length <= MaximoLogo && PrefijosLogo.Any(p => l.StartsWith(p, StringComparison.Ordinal))))
            .WithMessage("El logo debe ser una imagen PNG, JPG, WebP o SVG de máximo 300 KB.");
        RuleFor(x => x.NombreComercial).NotEmpty().WithMessage("Ingresa el nombre que aparece en los documentos.")
            .MaximumLength(150).WithMessage("El nombre admite máximo 150 caracteres.");
        RuleFor(x => x.Eslogan).MaximumLength(150).WithMessage("El eslogan admite máximo 150 caracteres.");
        RuleFor(x => x.Nit).MaximumLength(30).WithMessage("El NIT admite máximo 30 caracteres.");
        RuleFor(x => x.Direccion).MaximumLength(200).WithMessage("La dirección admite máximo 200 caracteres.");
        RuleFor(x => x.Ciudad).MaximumLength(100).WithMessage("La ciudad admite máximo 100 caracteres.");
        RuleFor(x => x.Telefono).MaximumLength(60).WithMessage("El teléfono admite máximo 60 caracteres.");
        RuleFor(x => x.Correo).CorreoOpcional();
        RuleFor(x => x.SitioWeb).MaximumLength(150).WithMessage("El sitio web admite máximo 150 caracteres.");
        RuleFor(x => x.ColorPrincipal).NotEmpty().WithMessage("Elige el color principal.")
            .Matches("^#[0-9A-Fa-f]{6}$").WithMessage("El color debe tener el formato #RRGGBB.");
        RuleFor(x => x.PiePagina).MaximumLength(500).WithMessage("El pie de página admite máximo 500 caracteres.");
        RuleFor(x => x.NotaFactura).MaximumLength(500).WithMessage("La nota admite máximo 500 caracteres.");
        RuleFor(x => x.NotaRecibo).MaximumLength(500).WithMessage("La nota admite máximo 500 caracteres.");
        RuleFor(x => x.NotaCotizacion).MaximumLength(500).WithMessage("La nota admite máximo 500 caracteres.");
    }
}

/// <summary>Formato de los PDF de la empresa del token (una fila por empresa).</summary>
public class ServicioFormatoDocumentos(IBaseDatos db)
{
    public const string ColorPorDefecto = "#1F2937";

    public async Task<FormatoDocumentosDto> ObtenerAsync(CancellationToken ct = default)
    {
        if (await db.FormatosDocumentos.AsNoTracking().FirstOrDefaultAsync(ct) is { } f)
        {
            return Dto(f);
        }
        var e = await db.Empresas.AsNoTracking().FirstOrDefaultAsync(ct) ?? throw ErrorNegocio.NoEncontrado("La empresa");
        return new FormatoDocumentosDto(
            "", e.Nombre, "", e.Nit, e.Direccion ?? "", e.Ciudad ?? "", e.Telefono ?? "", e.Email ?? "", "", ColorPorDefecto, "", "", "", "", false);
    }

    public async Task<FormatoDocumentosDto> ActualizarAsync(SolicitudFormatoDocumentos s, CancellationToken ct = default)
    {
        var f = await db.FormatosDocumentos.FirstOrDefaultAsync(ct);
        if (f is null)
        {
            f = new FormatoDocumentos();
            db.FormatosDocumentos.Add(f);
        }
        f.Logo = Texto.Opcional(s.Logo);
        f.NombreComercial = s.NombreComercial!.Trim();
        f.Eslogan = Texto.Opcional(s.Eslogan);
        f.Nit = Texto.Opcional(s.Nit);
        f.Direccion = Texto.Opcional(s.Direccion);
        f.Ciudad = Texto.Opcional(s.Ciudad);
        f.Telefono = Texto.Opcional(s.Telefono);
        f.Correo = Texto.Opcional(s.Correo)?.ToLowerInvariant();
        f.SitioWeb = Texto.Opcional(s.SitioWeb);
        f.ColorPrincipal = s.ColorPrincipal!.ToUpperInvariant();
        f.PiePagina = Texto.Opcional(s.PiePagina);
        f.NotaFactura = Texto.Opcional(s.NotaFactura);
        f.NotaRecibo = Texto.Opcional(s.NotaRecibo);
        f.NotaCotizacion = Texto.Opcional(s.NotaCotizacion);
        await db.SaveChangesAsync(ct);
        return Dto(f);
    }

    private static FormatoDocumentosDto Dto(FormatoDocumentos f) => new(
        f.Logo ?? "", f.NombreComercial ?? "", f.Eslogan ?? "", f.Nit ?? "", f.Direccion ?? "", f.Ciudad ?? "", f.Telefono ?? "", f.Correo ?? "",
        f.SitioWeb ?? "", f.ColorPrincipal, f.PiePagina ?? "", f.NotaFactura ?? "", f.NotaRecibo ?? "", f.NotaCotizacion ?? "", true);
}
