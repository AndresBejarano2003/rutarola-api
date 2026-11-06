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
