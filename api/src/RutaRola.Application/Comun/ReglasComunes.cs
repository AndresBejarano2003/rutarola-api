using FluentValidation;

namespace RutaRola.Application.Comun;

/// <summary>Reglas de validación compartidas (mismas que el frontend: <c>shared/formularios</c>, <c>personas/compartido/acceso.ts</c>).</summary>
public static class ReglasComunes
{
    public const string PatronUsuario = @"^\S{4,40}$";
    public const int ContrasenaMinima = 6;
    public const int ContrasenaMaxima = 64;
    public const string PatronNit = @"^\d{6,10}(-\d)?$";

    /// <summary>Teléfono opcional de 7 a 10 dígitos (se ignoran espacios, guiones y el prefijo +57).</summary>
    public static IRuleBuilderOptions<T, string?> TelefonoValido<T>(this IRuleBuilder<T, string?> regla) =>
        regla.Must(t => string.IsNullOrWhiteSpace(t) || EsTelefono(t)).WithMessage("Ingresa un teléfono válido (7 a 10 dígitos).");

    public static bool EsTelefono(string telefono) =>
        System.Text.RegularExpressions.Regex.IsMatch(
            System.Text.RegularExpressions.Regex.Replace(telefono.Replace(" ", "").Replace("-", ""), @"^\+?57", ""), @"^\d{7,10}$");

    public static IRuleBuilderOptions<T, string?> EstadoRegistroValido<T>(this IRuleBuilder<T, string?> regla) =>
        regla.Must(e => EstadoRegistro.Valores.Contains(e)).WithMessage("El estado debe ser ACTIVO o INACTIVO.");

    public static IRuleBuilderOptions<T, string?> CorreoOpcional<T>(this IRuleBuilder<T, string?> regla) =>
        regla.MaximumLength(150).Must(c => string.IsNullOrWhiteSpace(c) || EsCorreo(c)).WithMessage("Ingresa un correo válido.");

    /// <summary>Correo de contacto de una persona (máx. 100, como el formulario).</summary>
    public static IRuleBuilderOptions<T, string?> CorreoPersona<T>(this IRuleBuilder<T, string?> regla) =>
        regla.MaximumLength(100).WithMessage("El correo admite máximo 100 caracteres.")
            .Must(c => string.IsNullOrWhiteSpace(c) || EsCorreo(c)).WithMessage("Ingresa un correo válido.");

    /// <summary>Mismo patrón que <c>Validators.email</c> de Angular. Vacío = sin correo (el frontend envía <c>""</c>).</summary>
    public static bool EsCorreo(string correo) => System.Text.RegularExpressions.Regex.IsMatch(correo.Trim(),
        @"^(?=.{1,254}$)(?=.{1,64}@)[a-zA-Z0-9!#$%&'*+/=?^_`{|}~-]+(?:\.[a-zA-Z0-9!#$%&'*+/=?^_`{|}~-]+)*@[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*$");

    /// <summary>Fecha obligatoria que no puede ser posterior a hoy (ingreso, nacimiento).</summary>
    public static IRuleBuilderOptions<T, DateOnly?> FechaNoFutura<T>(this IRuleBuilder<T, DateOnly?> regla, string campo) =>
        regla.NotNull().WithMessage($"Ingresa la {campo}.")
            .Must(f => f is null || f <= Fechas.Hoy(TimeProvider.System))
            .WithMessage($"La {campo} no puede ser posterior a hoy.");

    public static IRuleBuilderOptions<T, string?> UsuarioValido<T>(this IRuleBuilder<T, string?> regla) =>
        regla.NotEmpty().WithMessage("Ingresa el usuario.")
            .Matches(PatronUsuario).WithMessage("El usuario debe tener entre 4 y 40 caracteres, sin espacios.");

    public static IRuleBuilderOptions<T, string?> ContrasenaValida<T>(this IRuleBuilder<T, string?> regla) =>
        regla.NotEmpty().WithMessage("Ingresa la contraseña.")
            .Length(ContrasenaMinima, ContrasenaMaxima)
            .WithMessage($"La contraseña debe tener entre {ContrasenaMinima} y {ContrasenaMaxima} caracteres.");
}
