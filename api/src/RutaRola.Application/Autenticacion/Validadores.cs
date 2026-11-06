using FluentValidation;

namespace RutaRola.Application.Autenticacion;

public class ValidadorSolicitudLogin : AbstractValidator<SolicitudLogin>
{
    public ValidadorSolicitudLogin()
    {
        RuleFor(x => x.Usuario).NotEmpty().WithMessage("Ingresa tu usuario y tu contraseña.").MaximumLength(150);
        RuleFor(x => x.Contrasena).NotEmpty().WithMessage("Ingresa tu usuario y tu contraseña.").MaximumLength(200);
    }
}

public class ValidadorSolicitudToken : AbstractValidator<SolicitudToken>
{
    public ValidadorSolicitudToken()
    {
        RuleFor(x => x.RefreshToken).NotEmpty().WithMessage("Falta el refresh token.").MaximumLength(200);
    }
}

public class ValidadorSolicitudCambioContrasena : AbstractValidator<SolicitudCambioContrasena>
{
    public ValidadorSolicitudCambioContrasena()
    {
        RuleFor(x => x.Actual).NotEmpty().WithMessage("Ingresa tu contraseña actual.").MaximumLength(200);
        RuleFor(x => x.Nueva).NotEmpty().WithMessage("Ingresa la nueva contraseña.")
            .MinimumLength(6).WithMessage("La nueva contraseña debe tener al menos 6 caracteres.")
            .MaximumLength(200).WithMessage("La nueva contraseña es demasiado larga.");
    }
}
