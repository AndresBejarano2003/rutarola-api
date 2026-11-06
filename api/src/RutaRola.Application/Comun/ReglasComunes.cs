using FluentValidation;

namespace RutaRola.Application.Comun;

/// <summary>Reglas de validación compartidas (mismas que el frontend: <c>shared/formularios</c>, <c>personas/compartido/acceso.ts</c>).</summary>
public static class ReglasComunes
{
    public const string PatronUsuario = @"^\S{4,40}$";
    public const int ContrasenaMinima = 6;
    public const int ContrasenaMaxima = 64;
    public const string PatronNit = @"^\d{6,10}(-\d)?$";

    public static IRuleBuilderOptions<T, string?> EstadoRegistroValido<T>(this IRuleBuilder<T, string?> regla) =>
        regla.Must(e => EstadoRegistro.Valores.Contains(e)).WithMessage("El estado debe ser ACTIVO o INACTIVO.");

    public static IRuleBuilderOptions<T, string?> CorreoOpcional<T>(this IRuleBuilder<T, string?> regla) =>
        regla.MaximumLength(150).EmailAddress().WithMessage("Ingresa un correo válido.");

    public static IRuleBuilderOptions<T, string?> UsuarioValido<T>(this IRuleBuilder<T, string?> regla) =>
        regla.NotEmpty().WithMessage("Ingresa el usuario.")
            .Matches(PatronUsuario).WithMessage("El usuario debe tener entre 4 y 40 caracteres, sin espacios.");

    public static IRuleBuilderOptions<T, string?> ContrasenaValida<T>(this IRuleBuilder<T, string?> regla) =>
        regla.NotEmpty().WithMessage("Ingresa la contraseña.")
            .Length(ContrasenaMinima, ContrasenaMaxima)
            .WithMessage($"La contraseña debe tener entre {ContrasenaMinima} y {ContrasenaMaxima} caracteres.");
}
