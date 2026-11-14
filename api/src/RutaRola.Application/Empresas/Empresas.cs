using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Application.Configuracion;
using RutaRola.Domain.Seguridad;

namespace RutaRola.Application.Empresas;

/// <summary><c>membresia</c>: plan y situación de la suscripción (solo en las consultas de la plataforma).</summary>
public record EmpresaDto(
    Guid Id, string Nombre, string Nit, string? Direccion, string? Ciudad, string? Telefono, string? Email,
    string? LogoUrl, string Estado, DateTime FechaCreacion, DateTime? FechaActualizacion,
    Suscripciones.MembresiaEmpresaDto? Membresia = null);

/// <summary>Empresas por situación de su membresía (tarjetas de la plataforma).</summary>
public record ResumenMembresiasDto(int Total, int Prueba, int PruebaPorVencer, int Activa, int PorVencer, int Vencida, int Gratis);

/// <summary>Primer administrador de la empresa (opcional al crearla).</summary>
public record AdministradorInicial(string? Nombre, string? Usuario, string? Email, string? Contrasena);

public record SolicitudEmpresa(
    string? Nombre, string? Nit, string? Direccion, string? Ciudad, string? Telefono, string? Email, string? LogoUrl,
    string? Estado, AdministradorInicial? Administrador = null);

/// <summary>Lo que el administrador puede cambiar de su propia empresa (no el NIT ni el estado).</summary>
public record SolicitudEmpresaPropia(string? Nombre, string? Direccion, string? Ciudad, string? Telefono, string? Email, string? LogoUrl);

/// <summary><c>membresia</c>: PRUEBA | PRUEBA_POR_VENCER | ACTIVA | POR_VENCER | VENCIDA | GRATIS; <c>plan</c>: plan vigente.</summary>
public record TemaDto(Domain.Enums.TemaApariencia Tema);

public record SolicitudTema(Domain.Enums.TemaApariencia? Tema);

public class ValidadorSolicitudTema : AbstractValidator<SolicitudTema>
{
    public ValidadorSolicitudTema() => RuleFor(x => x.Tema).NotNull().WithMessage("Elige el tema claro u oscuro.");
}

public record FiltrosEmpresa(string? Texto = null, string? Estado = null, string? Membresia = null, string? Plan = null);

public class ValidadorSolicitudEmpresa : AbstractValidator<SolicitudEmpresa>
{
    public ValidadorSolicitudEmpresa()
    {
        RuleFor(x => x.Nombre).NotEmpty().WithMessage("Ingresa el nombre de la empresa.").MaximumLength(150);
        RuleFor(x => x.Nit).NotEmpty().WithMessage("Ingresa el NIT.")
            .Matches(ReglasComunes.PatronNit).WithMessage("Ingresa un NIT válido (ej. 900123456-7).");
        RuleFor(x => x.Direccion).MaximumLength(200);
        RuleFor(x => x.Ciudad).MaximumLength(100);
        RuleFor(x => x.Telefono).MaximumLength(20);
        RuleFor(x => x.Email).CorreoOpcional();
        RuleFor(x => x.LogoUrl).MaximumLength(500);
        RuleFor(x => x.Estado).EstadoRegistroValido();
        RuleFor(x => x.Administrador!).ChildRules(a =>
        {
            a.RuleFor(x => x.Nombre).NotEmpty().WithMessage("Ingresa el nombre del administrador.").MaximumLength(150);
            a.RuleFor(x => x.Usuario).UsuarioValido();
            a.RuleFor(x => x.Email).CorreoOpcional();
            a.RuleFor(x => x.Contrasena).ContrasenaValida();
        }).When(x => x.Administrador is not null);
    }
}

public class ValidadorSolicitudEmpresaPropia : AbstractValidator<SolicitudEmpresaPropia>
{
    public ValidadorSolicitudEmpresaPropia()
    {
        RuleFor(x => x.Nombre).NotEmpty().WithMessage("Ingresa el nombre de la empresa.").MaximumLength(150);
        RuleFor(x => x.Direccion).MaximumLength(200);
        RuleFor(x => x.Ciudad).MaximumLength(100);
        RuleFor(x => x.Telefono).MaximumLength(20);
        RuleFor(x => x.Email).CorreoOpcional();
        RuleFor(x => x.LogoUrl).MaximumLength(500);
    }
}

/// <summary>
/// Empresas de transporte. Las operaciones de plataforma (listar, crear, editar cualquiera) son del
/// SuperAdmin y trabajan sin filtro de empresa; <c>actual</c> es la empresa del token.
/// </summary>
public class ServicioEmpresas(IBaseDatos db, IPasswordHasher<Usuario> hasher, TimeProvider reloj)
{
    private DateTime Ahora => reloj.GetUtcNow().UtcDateTime;

    public async Task<ListaPaginada<EmpresaDto>> ListarAsync(FiltrosEmpresa filtros, Paginacion paginacion, CancellationToken ct = default)
    {
        var consulta = db.Empresas.IgnoreQueryFilters([FiltrosConsulta.Empresa]).AsNoTracking();
        if (Texto.Opcional(filtros.Texto) is { } texto)
        {
            var busqueda = Busqueda.Normalizar(texto);
            consulta = consulta.Where(e => FuncionesBd.SinTildes(e.Nombre).Contains(busqueda) || e.Nit.Contains(busqueda));
        }
        if (Texto.Opcional(filtros.Estado) is { } estado)
        {
            var activo = EstadoRegistro.EsActivo(estado);
            consulta = consulta.Where(e => e.Activo == activo);
        }
        if (Texto.Opcional(filtros.Membresia) is { } situacion)
        {
            var ids = await IdsConSituacionAsync(situacion, ct);
            consulta = consulta.Where(e => ids.Contains(e.Id));
        }
        if (TextoEnum.Leer<Domain.Enums.PlanSuscripcion>(filtros.Plan) is { } plan)
        {
            var ahora = Ahora;
            var suscripciones = db.Suscripciones.IgnoreQueryFilters([FiltrosConsulta.Empresa]);
            // Plan vigente: el vencido (sin bajar aún) cuenta como Gratis.
            consulta = plan == Domain.Enums.PlanSuscripcion.Gratis
                ? consulta.Where(e => suscripciones.Any(s => s.IdEmpresa == e.Id && (s.Plan == plan
                    || (s.Estado != Domain.Enums.EstadoSuscripcion.Gratis && (s.FechaFin == null || s.FechaFin <= ahora)))))
                : consulta.Where(e => suscripciones.Any(s => s.IdEmpresa == e.Id && s.Plan == plan
                    && (s.Estado == Domain.Enums.EstadoSuscripcion.Gratis || s.FechaFin > ahora)));
        }

        var total = await consulta.CountAsync(ct);
        var items = await consulta.OrderBy(e => e.Nombre)
            .Skip(paginacion.Omitir).Take(paginacion.TamanoValido)
            .Select(Proyeccion).ToListAsync(ct);
        return new ListaPaginada<EmpresaDto>(await ConMembresiaAsync(items, ct), total, paginacion.PaginaValida, paginacion.TamanoValido);
    }

    /// <summary>Cuántas empresas hay en cada situación de membresía.</summary>
    public async Task<ResumenMembresiasDto> ResumenMembresiasAsync(CancellationToken ct = default)
    {
        var ahora = Ahora;
        var ids = await db.Empresas.IgnoreQueryFilters([FiltrosConsulta.Empresa]).Select(e => e.Id).ToListAsync(ct);
        var suscripciones = await db.Suscripciones.IgnoreQueryFilters([FiltrosConsulta.Empresa]).AsNoTracking().ToDictionaryAsync(s => s.IdEmpresa, ct);
        var situaciones = ids.Select(id => Suscripciones.ServicioSuscripcion.Situacion(suscripciones.GetValueOrDefault(id), ahora)).ToList();
        int Contar(string s) => situaciones.Count(x => x == s);
        return new ResumenMembresiasDto(ids.Count, Contar("PRUEBA"), Contar("PRUEBA_POR_VENCER"), Contar("ACTIVA"), Contar("POR_VENCER"),
            Contar("VENCIDA"), Contar("GRATIS"));
    }

    public async Task<EmpresaDto> ObtenerAsync(Guid id, CancellationToken ct = default)
    {
        var empresa = await db.Empresas.IgnoreQueryFilters([FiltrosConsulta.Empresa]).Where(e => e.Id == id).Select(Proyeccion).FirstOrDefaultAsync(ct)
            ?? throw ErrorNegocio.NoEncontrado("La empresa");
        return (await ConMembresiaAsync([empresa], ct))[0];
    }

    private async Task<IReadOnlyList<EmpresaDto>> ConMembresiaAsync(IReadOnlyList<EmpresaDto> empresas, CancellationToken ct)
    {
        var ids = empresas.Select(e => e.Id).ToList();
        var suscripciones = await db.Suscripciones.IgnoreQueryFilters([FiltrosConsulta.Empresa]).AsNoTracking()
            .Where(s => ids.Contains(s.IdEmpresa)).ToDictionaryAsync(s => s.IdEmpresa, ct);
        var ahora = Ahora;
        return empresas.Select(e => e with { Membresia = Suscripciones.ServicioSuscripcion.Membresia(suscripciones.GetValueOrDefault(e.Id), ahora) }).ToList();
    }

    /// <summary>Ids de las empresas en una situación (se calcula en memoria: son pocas filas por empresa).</summary>
    private async Task<List<Guid>> IdsConSituacionAsync(string situacion, CancellationToken ct)
    {
        var ahora = Ahora;
        var ids = await db.Empresas.IgnoreQueryFilters([FiltrosConsulta.Empresa]).Select(e => e.Id).ToListAsync(ct);
        var suscripciones = await db.Suscripciones.IgnoreQueryFilters([FiltrosConsulta.Empresa]).AsNoTracking().ToDictionaryAsync(s => s.IdEmpresa, ct);
        return ids.Where(id => Suscripciones.ServicioSuscripcion.Situacion(suscripciones.GetValueOrDefault(id), ahora) == situacion).ToList();
    }

    /// <param name="comienzo">
    /// PRUEBA (por defecto, también al crearla la plataforma), GRATIS (registro en el plan Gratis, sin vencimiento) o
    /// PAGAR (registro con "Pagar un plan ahora": en Gratis con la pasarela pendiente).
    /// </param>
    public async Task<EmpresaDto> CrearAsync(SolicitudEmpresa solicitud, CancellationToken ct = default,
        Domain.Enums.ComienzoRegistro comienzo = Domain.Enums.ComienzoRegistro.Prueba)
    {
        db.UsarModoSistema();
        await ValidarNitLibreAsync(solicitud.Nit!, null, ct);

        var empresa = new Empresa();
        Aplicar(empresa, solicitud);
        db.Empresas.Add(empresa);
        // La prueba toma los días que dice HOY la plataforma (los conserva aunque el parámetro cambie).
        db.Suscripciones.Add(comienzo switch
        {
            Domain.Enums.ComienzoRegistro.Gratis => Suscripciones.ServicioSuscripcion.Gratis(empresa.Id, Ahora),
            Domain.Enums.ComienzoRegistro.Pagar => Suscripciones.ServicioSuscripcion.SinPrueba(empresa.Id, Ahora),
            _ => Suscripciones.ServicioSuscripcion.Prueba(empresa.Id, Ahora, await Suscripciones.ServicioParametrosPlataforma.DiasPruebaAsync(db, ct)),
        });
        foreach (var parametro in CatalogoConfiguracion.Parametros)
        {
            db.ConfiguracionSistema.Add(new ConfiguracionSistema { IdEmpresa = empresa.Id, Clave = parametro.Clave, Valor = parametro.ValorPorDefecto });
        }

        if (solicitud.Administrador is { } admin)
        {
            var usuario = Texto.Usuario(admin.Usuario);
            var correo = Texto.Opcional(admin.Email)?.ToLowerInvariant();
            if (await db.Usuarios.IgnoreQueryFilters([FiltrosConsulta.Empresa]).AnyAsync(u => u.NombreUsuario == usuario || (correo != null && u.Email == correo), ct))
            {
                throw new ErrorNegocio("Ya existe un usuario con ese nombre de usuario o correo.", CodigoError.Duplicado);
            }
            var rol = await db.Roles.SingleAsync(r => r.Nombre == Roles.AdminEmpresa, ct);
            var nuevo = new Usuario { IdEmpresa = empresa.Id, IdRol = rol.Id, Nombre = admin.Nombre!.Trim(), NombreUsuario = usuario, Email = correo };
            nuevo.PasswordHash = hasher.HashPassword(nuevo, admin.Contrasena!);
            db.Usuarios.Add(nuevo);
        }

        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(empresa.Id, ct);
    }

    public async Task<EmpresaDto> ActualizarAsync(Guid id, SolicitudEmpresa solicitud, CancellationToken ct = default)
    {
        db.UsarModoSistema();
        var empresa = await db.Empresas.FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw ErrorNegocio.NoEncontrado("La empresa");
        await ValidarNitLibreAsync(solicitud.Nit!, id, ct);
        Aplicar(empresa, solicitud);
        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(id, ct);
    }

    /// <summary>Empresa del token (404 si el usuario no tiene empresa, p. ej. SuperAdmin).</summary>
    public async Task<EmpresaDto> ObtenerActualAsync(CancellationToken ct = default) =>
        await db.Empresas.Select(Proyeccion).FirstOrDefaultAsync(ct) ?? throw ErrorNegocio.NoEncontrado("La empresa");

    /// <summary>Tema de la aplicación para los usuarios de la empresa del token (lo trae la sesión).</summary>
    public async Task<TemaDto> CambiarTemaAsync(SolicitudTema s, CancellationToken ct = default)
    {
        var empresa = await db.Empresas.FirstOrDefaultAsync(ct) ?? throw ErrorNegocio.NoEncontrado("La empresa");
        empresa.Tema = s.Tema!.Value;
        await db.SaveChangesAsync(ct);
        return new TemaDto(empresa.Tema);
    }

    public async Task<TemaDto> TemaAsync(CancellationToken ct = default) =>
        new(await db.Empresas.Select(e => e.Tema).FirstOrDefaultAsync(ct));

    public async Task<EmpresaDto> ActualizarActualAsync(SolicitudEmpresaPropia solicitud, CancellationToken ct = default)
    {
        var empresa = await db.Empresas.FirstOrDefaultAsync(ct) ?? throw ErrorNegocio.NoEncontrado("La empresa");
        empresa.Nombre = solicitud.Nombre!.Trim();
        empresa.Direccion = Texto.Opcional(solicitud.Direccion);
        empresa.Ciudad = Texto.Opcional(solicitud.Ciudad);
        empresa.Telefono = Texto.Opcional(solicitud.Telefono);
        empresa.Email = Texto.Opcional(solicitud.Email)?.ToLowerInvariant();
        empresa.LogoUrl = Texto.Opcional(solicitud.LogoUrl);
        await db.SaveChangesAsync(ct);
        return await ObtenerActualAsync(ct);
    }

    private async Task ValidarNitLibreAsync(string nit, Guid? idActual, CancellationToken ct)
    {
        var limpio = nit.Trim();
        if (await db.Empresas.IgnoreQueryFilters([FiltrosConsulta.Empresa]).AnyAsync(e => e.Nit == limpio && e.Id != idActual, ct))
        {
            throw new ErrorNegocio($"Ya existe una empresa con el NIT {limpio}.", CodigoError.Duplicado);
        }
    }

    private static void Aplicar(Empresa empresa, SolicitudEmpresa s)
    {
        empresa.Nombre = s.Nombre!.Trim();
        empresa.Nit = s.Nit!.Trim();
        empresa.Direccion = Texto.Opcional(s.Direccion);
        empresa.Ciudad = Texto.Opcional(s.Ciudad);
        empresa.Telefono = Texto.Opcional(s.Telefono);
        empresa.Email = Texto.Opcional(s.Email)?.ToLowerInvariant();
        empresa.LogoUrl = Texto.Opcional(s.LogoUrl);
        empresa.Activo = EstadoRegistro.EsActivo(s.Estado);
    }

    private static readonly System.Linq.Expressions.Expression<Func<Empresa, EmpresaDto>> Proyeccion = e => new EmpresaDto(
        e.Id, e.Nombre, e.Nit, e.Direccion, e.Ciudad, e.Telefono, e.Email, e.LogoUrl,
        e.Activo ? EstadoRegistro.Activo : EstadoRegistro.Inactivo, e.FechaCreacion, e.FechaActualizacion);
}
