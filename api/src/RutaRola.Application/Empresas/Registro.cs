using FluentValidation;
using RutaRola.Application.Autenticacion;
using RutaRola.Application.Comun;

namespace RutaRola.Application.Empresas;

public record EmpresaRegistro(string? Nombre, string? Nit, string? Ciudad, string? Telefono);

public record AdministradorRegistro(string? Nombre, string? Usuario, string? Email, string? Contrasena);

/// <summary>
/// Registro público de una empresa de transporte con su administrador (pantalla <c>/registro</c>).
/// <c>pagarAhora</c>: sin prueba gratuita; entra en Gratis y la pantalla lo lleva a pagar el plan elegido.
/// </summary>
public record SolicitudRegistro(EmpresaRegistro? Empresa, AdministradorRegistro? Administrador, bool AceptaTerminos, bool PagarAhora = false);

public class ValidadorSolicitudRegistro : AbstractValidator<SolicitudRegistro>
{
    public ValidadorSolicitudRegistro()
    {
        RuleFor(x => x.Empresa).NotNull().WithMessage("Ingresa los datos de la empresa.");
        RuleFor(x => x.Empresa!).ChildRules(e =>
        {
            e.RuleFor(x => x.Nombre).NotEmpty().WithMessage("Ingresa el nombre de la empresa.").MaximumLength(150);
            e.RuleFor(x => x.Nit).NotEmpty().WithMessage("Ingresa el NIT.")
                .Matches(ReglasComunes.PatronNit).WithMessage("Ingresa un NIT válido (ej. 900123456-7).");
            e.RuleFor(x => x.Ciudad).NotEmpty().WithMessage("Ingresa la ciudad.").MaximumLength(100);
            e.RuleFor(x => x.Telefono).NotEmpty().WithMessage("Ingresa un teléfono de contacto.").MaximumLength(20);
        }).When(x => x.Empresa is not null);
        RuleFor(x => x.Administrador).NotNull().WithMessage("Ingresa los datos del administrador.");
        RuleFor(x => x.Administrador!).ChildRules(a =>
        {
            a.RuleFor(x => x.Nombre).NotEmpty().WithMessage("Ingresa tu nombre.").MaximumLength(150);
            a.RuleFor(x => x.Usuario).UsuarioValido();
            a.RuleFor(x => x.Email).NotEmpty().WithMessage("Ingresa tu correo: allí te avisamos de tu cuenta.").CorreoOpcional();
            a.RuleFor(x => x.Contrasena).ContrasenaValida();
        }).When(x => x.Administrador is not null);
        RuleFor(x => x.AceptaTerminos).Equal(true).WithMessage("Debes aceptar los términos y la política de datos.");
    }
}

/// <summary>
/// Crea la empresa (con la prueba gratuita del plan Profesional por los días de
/// <see cref="Suscripciones.ServicioParametrosPlataforma.DiasPrueba"/>, o sin prueba si eligió pagar de una vez) y su
/// administrador, y deja la sesión iniciada. 409 si el NIT, el usuario o el correo ya existen.
/// </summary>
public class ServicioRegistro(ServicioEmpresas empresas, ServicioAutenticacion autenticacion)
{
    public async Task<RespuestaLogin> RegistrarAsync(SolicitudRegistro s, InfoCliente cliente, CancellationToken ct = default)
    {
        var e = s.Empresa!;
        var a = s.Administrador!;
        await empresas.CrearAsync(new SolicitudEmpresa(
            e.Nombre!.Trim(), e.Nit!.Trim(), null, e.Ciudad!.Trim(), e.Telefono!.Trim(), a.Email!.Trim(), null, EstadoRegistro.Activo,
            new AdministradorInicial(a.Nombre!.Trim(), a.Usuario, a.Email, a.Contrasena)), ct, s.PagarAhora);
        return await autenticacion.IniciarSesionAsync(new SolicitudLogin(Texto.Usuario(a.Usuario), a.Contrasena), cliente, ct);
    }
}
