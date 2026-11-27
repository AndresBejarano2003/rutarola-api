using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RutaRola.Application.Comun;
using RutaRola.Application.Configuracion;
using RutaRola.Domain.Seguridad;

namespace RutaRola.Application.Autenticacion;

/// <summary>Una cuenta del grupo del acudiente (la de la sesión incluida).</summary>
/// <param name="Disponible">Se puede entrar: usuario, acudiente y empresa activos y la empresa permite unir cuentas.</param>
/// <param name="Motivo">Por qué no está disponible (vacío si lo está).</param>
/// <param name="Estudiantes">Nombres de sus estudiantes activos en esa empresa (resumen; el detalle se ve al entrar).</param>
public record CuentaUnidaDto(
    Guid IdUsuario, string Usuario, string Nombre, Guid IdEmpresa, string Empresa, bool Actual, bool Disponible, string Motivo,
    IReadOnlyList<string> Estudiantes);

public record SolicitudUnirCuenta(string? Usuario, string? Contrasena);

public class ValidadorSolicitudUnirCuenta : AbstractValidator<SolicitudUnirCuenta>
{
    public ValidadorSolicitudUnirCuenta()
    {
        RuleFor(x => x.Usuario).NotEmpty().WithMessage("Escribe el usuario de la otra cuenta.").MaximumLength(150);
        RuleFor(x => x.Contrasena).NotEmpty().WithMessage("Escribe la contraseña de la otra cuenta.").MaximumLength(200);
    }
}

/// <summary>
/// Cuentas unidas de un acudiente con estudiantes en varias empresas (tabla <c>cuentas_unidas</c>). Reglas:
/// - solo acudientes; se une una cuenta de OTRA empresa escribiendo su usuario y contraseña (prueba que es suya);
/// - las dos empresas deben permitirlo (<c>portal.acudientes_unen_cuentas</c>, por defecto 1);
/// - unir dos cuentas que ya tienen grupo junta los grupos; separar saca una cuenta del grupo;
/// - cambiar de cuenta abre una sesión de esa empresa (token con SU empresa: el aislamiento no cambia) y cierra la actual;
/// - ninguna empresa ve los datos de otra: aquí solo se leen el nombre de la empresa y los nombres de los estudiantes
///   del propio acudiente, para que sepa qué hay en cada cuenta.
/// </summary>
public class ServicioCuentasUnidas(
    IBaseDatos db,
    IContextoUsuario contexto,
    IPasswordHasher<Usuario> hasher,
    ServicioAutenticacion autenticacion,
    TimeProvider reloj,
    ILogger<ServicioCuentasUnidas> logger)
{
    public const string MensajeSoloAcudientes = "Solo los acudientes pueden unir cuentas.";
    public const string MensajeNoPermite = "no permite unir cuentas de acudientes";

    public async Task<IReadOnlyList<CuentaUnidaDto>> ListarAsync(CancellationToken ct = default)
    {
        var yo = Acudiente();
        var ids = await IdsDelGrupoAsync(yo, ct);
        var usuarios = await db.Usuarios.IgnoreQueryFilters([FiltrosConsulta.Empresa]).AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.NombreUsuario, u.Activo, u.IdEmpresa, Empresa = u.Empresa!.Nombre, EmpresaActiva = u.Empresa.Activo })
            .ToListAsync(ct);
        var acudientes = await db.Acudientes.IgnoreQueryFilters([FiltrosConsulta.Empresa]).AsNoTracking()
            .Where(a => a.IdUsuario != null && ids.Contains(a.IdUsuario.Value))
            .Select(a => new
            {
                a.Id, IdUsuario = a.IdUsuario!.Value, a.Activo, a.PrimerNombre, a.SegundoNombre, a.PrimerApellido, a.SegundoApellido,
            })
            .ToListAsync(ct);
        var idsAcudientes = acudientes.Select(a => a.Id).ToList();
        var estudiantes = (await db.EstudiantesAcudientes.IgnoreQueryFilters([FiltrosConsulta.Empresa]).AsNoTracking()
                .Where(ea => idsAcudientes.Contains(ea.IdAcudiente) && ea.Estudiante.Activo)
                .Select(ea => new { ea.IdAcudiente, Nombre = ea.Estudiante.PrimerNombre + " " + ea.Estudiante.PrimerApellido })
                .ToListAsync(ct))
            .GroupBy(x => x.IdAcudiente)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.Nombre).Order().ToList());
        var permiten = await PermitenAsync(usuarios.Select(u => u.IdEmpresa ?? Guid.Empty), ct);

        return usuarios.Select(u =>
        {
            var a = acudientes.FirstOrDefault(x => x.IdUsuario == u.Id);
            var motivo = !u.Activo || a is not { Activo: true } ? "La cuenta está inactiva en esa empresa."
                : !u.EmpresaActiva ? "La empresa está inactiva."
                : !permiten.Contains(u.IdEmpresa ?? Guid.Empty) ? $"{u.Empresa} {MensajeNoPermite}."
                : "";
            var nombre = a is null ? u.NombreUsuario
                : string.Join(' ', new[] { a.PrimerNombre, a.SegundoNombre, a.PrimerApellido, a.SegundoApellido }.Where(x => !string.IsNullOrWhiteSpace(x)));
            return new CuentaUnidaDto(u.Id, u.NombreUsuario, nombre, u.IdEmpresa ?? Guid.Empty, u.Empresa, u.Id == yo, motivo == "", motivo,
                a is not null && estudiantes.TryGetValue(a.Id, out var nombres) ? nombres : []);
        })
            .OrderByDescending(c => c.Actual).ThenBy(c => c.Empresa)
            .ToList();
    }

    /// <summary>Une la cuenta de otra empresa cuyo usuario y contraseña se escriben. Devuelve el grupo actualizado.</summary>
    public async Task<IReadOnlyList<CuentaUnidaDto>> UnirAsync(SolicitudUnirCuenta s, CancellationToken ct = default)
    {
        var yo = Acudiente();
        var miEmpresa = contexto.IdEmpresa ?? Guid.Empty;
        var usuarioOCorreo = Texto.Usuario(s.Usuario);
        var otro = await db.Usuarios.IgnoreQueryFilters([FiltrosConsulta.Empresa]).Include(u => u.Rol).Include(u => u.Empresa)
            .FirstOrDefaultAsync(u => u.NombreUsuario == usuarioOCorreo || u.Email == usuarioOCorreo, ct);
        if (otro is null || hasher.VerifyHashedPassword(otro, otro.PasswordHash, s.Contrasena ?? "") == PasswordVerificationResult.Failed)
        {
            logger.LogInformation("Unir cuentas rechazado para {IdUsuario}: credenciales", yo);
            throw new ErrorNegocio("Usuario o contraseña de la otra cuenta incorrectos.", CodigoError.Validacion);
        }
        if (otro.Id == yo)
        {
            throw new ErrorNegocio("Esa es la cuenta con la que ya iniciaste sesión.", CodigoError.Validacion);
        }
        if (otro.Rol.Nombre != Roles.Acudiente)
        {
            throw new ErrorNegocio("Solo se pueden unir cuentas de acudiente.", CodigoError.Validacion);
        }
        if (otro.IdEmpresa == miEmpresa)
        {
            throw new ErrorNegocio("Esa cuenta es de tu misma empresa de transporte: pídele al administrador que deje todos tus estudiantes en un solo acudiente.", CodigoError.Validacion);
        }
        if (!otro.Activo || otro.Empresa is not { Activo: true }
            || !await db.Acudientes.IgnoreQueryFilters([FiltrosConsulta.Empresa]).AnyAsync(a => a.IdUsuario == otro.Id && a.Activo, ct))
        {
            throw new ErrorNegocio("Esa cuenta está inactiva en su empresa de transporte.", CodigoError.Validacion);
        }
        var permiten = await PermitenAsync([miEmpresa, otro.IdEmpresa ?? Guid.Empty], ct);
        foreach (var (empresa, nombre) in new[] { (miEmpresa, "Tu empresa de transporte"), (otro.IdEmpresa ?? Guid.Empty, otro.Empresa.Nombre) })
        {
            if (!permiten.Contains(empresa))
            {
                throw new ErrorNegocio($"{nombre} {MensajeNoPermite}.", CodigoError.Validacion);
            }
        }

        var filas = await db.CuentasUnidas.IgnoreQueryFilters([FiltrosConsulta.Empresa])
            .Where(c => c.IdUsuario == yo || c.IdUsuario == otro.Id).ToListAsync(ct);
        var mia = filas.FirstOrDefault(c => c.IdUsuario == yo);
        var suya = filas.FirstOrDefault(c => c.IdUsuario == otro.Id);
        if (mia is not null && suya is not null && mia.IdGrupo == suya.IdGrupo)
        {
            throw new ErrorNegocio("Esa cuenta ya está unida a la tuya.", CodigoError.Duplicado);
        }

        var grupo = mia?.IdGrupo ?? suya?.IdGrupo ?? Guid.CreateVersion7();
        var ahora = reloj.GetUtcNow().UtcDateTime;
        if (mia is null)
        {
            db.CuentasUnidas.Add(new CuentaUnida { IdGrupo = grupo, IdUsuario = yo, FechaCreacion = ahora });
        }
        if (suya is null)
        {
            db.CuentasUnidas.Add(new CuentaUnida { IdGrupo = grupo, IdUsuario = otro.Id, FechaCreacion = ahora });
        }
        else if (suya.IdGrupo != grupo)
        {
            // Las dos ya tenían grupo: se juntan en el de la sesión.
            var deSuGrupo = await db.CuentasUnidas.IgnoreQueryFilters([FiltrosConsulta.Empresa])
                .Where(c => c.IdGrupo == suya.IdGrupo).ToListAsync(ct);
            foreach (var c in deSuGrupo)
            {
                c.IdGrupo = grupo;
            }
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Cuentas unidas: {IdUsuario} con {IdOtro}", yo, otro.Id);
        return await ListarAsync(ct);
    }

    /// <summary>Saca una cuenta del grupo (la propia o otra del grupo). Un grupo de una sola cuenta desaparece.</summary>
    public async Task<IReadOnlyList<CuentaUnidaDto>> SepararAsync(Guid idUsuario, CancellationToken ct = default)
    {
        var yo = Acudiente();
        var filas = await FilasDelGrupoAsync(yo, ct);
        var fila = filas.FirstOrDefault(c => c.IdUsuario == idUsuario)
            ?? throw new ErrorNegocio("Esa cuenta no está unida a la tuya.", CodigoError.NoEncontrado);
        db.CuentasUnidas.Remove(fila);
        var quedan = filas.Where(c => c != fila).ToList();
        if (quedan.Count == 1)
        {
            db.CuentasUnidas.Remove(quedan[0]);
        }
        await db.SaveChangesAsync(ct);
        return await ListarAsync(ct);
    }

    /// <summary>Entra a otra cuenta del grupo: sesión y tokens de esa empresa; la sesión actual se cierra.</summary>
    public async Task<RespuestaLogin> CambiarAsync(Guid idUsuario, InfoCliente cliente, CancellationToken ct = default)
    {
        var yo = Acudiente();
        if (idUsuario == yo)
        {
            throw new ErrorNegocio("Ya estás en esa cuenta.", CodigoError.Validacion);
        }
        var cuenta = (await ListarAsync(ct)).FirstOrDefault(c => c.IdUsuario == idUsuario)
            ?? throw new ErrorNegocio("Esa cuenta no está unida a la tuya.", CodigoError.NoEncontrado);
        if (!cuenta.Disponible)
        {
            throw new ErrorNegocio(cuenta.Motivo, CodigoError.Validacion);
        }
        var respuesta = await autenticacion.AbrirSesionUnidaAsync(idUsuario, cliente, ct);
        await autenticacion.CerrarSesionAsync(null, contexto.IdSesion, ct);
        return respuesta;
    }

    private Guid Acudiente()
    {
        if (contexto.Rol != Roles.Acudiente || contexto.IdUsuario is not { } yo)
        {
            throw new ErrorNegocio(MensajeSoloAcudientes, CodigoError.Prohibido);
        }
        return yo;
    }

    private async Task<List<CuentaUnida>> FilasDelGrupoAsync(Guid yo, CancellationToken ct)
    {
        var mia = await db.CuentasUnidas.IgnoreQueryFilters([FiltrosConsulta.Empresa]).FirstOrDefaultAsync(c => c.IdUsuario == yo, ct);
        return mia is null ? [] : await db.CuentasUnidas.IgnoreQueryFilters([FiltrosConsulta.Empresa]).Where(c => c.IdGrupo == mia.IdGrupo).ToListAsync(ct);
    }

    private async Task<List<Guid>> IdsDelGrupoAsync(Guid yo, CancellationToken ct)
    {
        var ids = (await FilasDelGrupoAsync(yo, ct)).Select(c => c.IdUsuario).ToList();
        return ids.Count == 0 ? [yo] : ids;
    }

    /// <summary>Empresas (de la lista) que permiten unir cuentas: sin la fila del parámetro vale el defecto (1).</summary>
    private async Task<HashSet<Guid>> PermitenAsync(IEnumerable<Guid> empresas, CancellationToken ct)
    {
        var lista = empresas.Distinct().ToList();
        var apagadas = await db.ConfiguracionSistema.IgnoreQueryFilters([FiltrosConsulta.Empresa]).AsNoTracking()
            .Where(c => c.Clave == CatalogoConfiguracion.AcudientesUnenCuentas && lista.Contains(c.IdEmpresa) && c.Valor == "0")
            .Select(c => c.IdEmpresa).ToListAsync(ct);
        return lista.Except(apagadas).ToHashSet();
    }
}
