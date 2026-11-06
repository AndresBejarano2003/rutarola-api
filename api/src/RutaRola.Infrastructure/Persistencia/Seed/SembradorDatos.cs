using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RutaRola.Application.Configuracion;
using RutaRola.Domain.Seguridad;

namespace RutaRola.Infrastructure.Persistencia.Seed;

/// <summary>
/// Datos base, idempotente (se puede correr en cada despliegue):
/// - permisos y roles base, con <c>roles_permisos</c> sincronizado con <see cref="Permisos.PorRol"/>;
/// - empresa demo y su configuración por defecto;
/// - administrador demo (<c>Seed:Admin:*</c>) y SuperAdmin opcional (<c>Seed:SuperAdmin:*</c>).
/// - datos de ejemplo (<see cref="DatosDemo"/>) si <c>Seed:Demo:Habilitado</c> (Development/compose; en producción
/// además exige <c>Seed:Demo:PermitirEnProduccion</c>, solo para un despliegue de demostración).
/// Las contraseñas SOLO vienen de variables de entorno / user-secrets; si faltan, ese usuario no se crea.
/// Nunca cambia la contraseña de un usuario que ya existe.
/// </summary>
public class SembradorDatos(
    RutaRolaDbContext db,
    IPasswordHasher<Usuario> hasher,
    IConfiguration configuracion,
    TimeProvider reloj,
    ILogger<SembradorDatos> logger)
{
    private static readonly Dictionary<string, string> DescripcionRoles = new()
    {
        [Roles.SuperAdmin] = "Administra la plataforma y las empresas de transporte",
        [Roles.AdminEmpresa] = "Administra todos los módulos de su empresa",
        [Roles.Conductor] = "Ruta del día: recorridos asignados y GPS",
        [Roles.Monitora] = "Ruta del día: recorridos asignados y orden de visita",
        [Roles.Acudiente] = "Portal del acudiente: solo sus estudiantes (lectura)",
    };

    public async Task SembrarAsync(CancellationToken ct = default)
    {
        db.UsarModoSistema();
        var roles = await SembrarRolesYPermisosAsync(ct);
        var empresa = await SembrarEmpresaDemoAsync(ct);
        await SembrarUsuarioAsync("Admin", roles[Roles.AdminEmpresa], empresa.Id, "Administrador demo", ct);
        await SembrarUsuarioAsync("SuperAdmin", roles[Roles.SuperAdmin], null, "Super administrador", ct);
        await SembrarDemoAsync(empresa, roles, ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task SembrarDemoAsync(Empresa empresa, IReadOnlyDictionary<string, Rol> roles, CancellationToken ct)
    {
        var seccion = configuracion.GetSection("Seed:Demo");
        if (!seccion.GetValue("Habilitado", false))
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(seccion["Password"]))
        {
            logger.LogWarning("No se crearon los datos demo: falta Seed__Demo__Password (variable de entorno o user-secrets).");
            return;
        }
        if (await new DatosDemo(db, hasher, reloj).SembrarAsync(empresa, roles, seccion["Password"]!, ct))
        {
            logger.LogInformation("Datos demo creados en {Empresa}", empresa.Nombre);
        }
    }

    private async Task<Dictionary<string, Rol>> SembrarRolesYPermisosAsync(CancellationToken ct)
    {
        var permisos = await db.Permisos.ToDictionaryAsync(p => p.Nombre, ct);
        foreach (var (nombre, descripcion) in Permisos.Descripciones)
        {
            if (permisos.TryGetValue(nombre, out var existente))
            {
                existente.Descripcion = descripcion;
            }
            else
            {
                permisos[nombre] = db.Permisos.Add(new Permiso { Nombre = nombre, Descripcion = descripcion }).Entity;
            }
        }

        var roles = await db.Roles.Include(r => r.Permisos).ToDictionaryAsync(r => r.Nombre, ct);
        foreach (var (nombre, descripcion) in DescripcionRoles)
        {
            if (!roles.TryGetValue(nombre, out var rol))
            {
                rol = db.Roles.Add(new Rol { Nombre = nombre }).Entity;
                roles[nombre] = rol;
            }
            rol.Descripcion = descripcion;

            var deseados = Permisos.PorRol[nombre].Select(p => permisos[p].Id).ToHashSet();
            rol.Permisos.RemoveAll(rp => !deseados.Contains(rp.IdPermiso));
            foreach (var id in deseados.Except(rol.Permisos.Select(rp => rp.IdPermiso)))
            {
                rol.Permisos.Add(new RolPermiso { IdRol = rol.Id, IdPermiso = id });
            }
        }
        return roles;
    }

    private async Task<Empresa> SembrarEmpresaDemoAsync(CancellationToken ct)
    {
        var seccion = configuracion.GetSection("Seed:Empresa");
        var nit = seccion["Nit"] ?? "900000001-1";
        var empresa = await db.Empresas.FirstOrDefaultAsync(e => e.Nit == nit, ct);
        if (empresa is null)
        {
            empresa = db.Empresas.Add(new Empresa
            {
                Nombre = seccion["Nombre"] ?? "RutaRola Demo",
                Nit = nit,
                Ciudad = seccion["Ciudad"] ?? "Bogotá",
                Email = seccion["Email"] ?? "contacto@rutarola.com",
            }).Entity;
            logger.LogInformation("Empresa demo creada ({Nit})", nit);
        }

        var claves = await db.ConfiguracionSistema.Where(c => c.IdEmpresa == empresa.Id).Select(c => c.Clave).ToListAsync(ct);
        foreach (var parametro in CatalogoConfiguracion.Parametros.Where(p => !claves.Contains(p.Clave)))
        {
            db.ConfiguracionSistema.Add(new ConfiguracionSistema { IdEmpresa = empresa.Id, Clave = parametro.Clave, Valor = parametro.ValorPorDefecto });
        }
        return empresa;
    }

    private async Task SembrarUsuarioAsync(string seccionNombre, Rol rol, Guid? idEmpresa, string nombrePorDefecto, CancellationToken ct)
    {
        var seccion = configuracion.GetSection($"Seed:{seccionNombre}");
        var usuario = (seccion["Usuario"] ?? (seccionNombre == "Admin" ? "admin" : "superadmin")).Trim().ToLowerInvariant();
        if (await db.Usuarios.AnyAsync(u => u.NombreUsuario == usuario, ct))
        {
            return;
        }

        var contrasena = seccion["Password"];
        if (string.IsNullOrWhiteSpace(contrasena))
        {
            logger.LogWarning(
                "No se creó el usuario '{Usuario}': falta Seed__{Seccion}__Password (variable de entorno o user-secrets).",
                usuario, seccionNombre);
            return;
        }

        var nuevo = new Usuario
        {
            IdEmpresa = idEmpresa,
            IdRol = rol.Id,
            Nombre = seccion["Nombre"] ?? nombrePorDefecto,
            NombreUsuario = usuario,
            Email = seccion["Email"]?.Trim().ToLowerInvariant(),
        };
        nuevo.PasswordHash = hasher.HashPassword(nuevo, contrasena);
        db.Usuarios.Add(nuevo);
        logger.LogInformation("Usuario {Usuario} ({Rol}) creado", usuario, rol.Nombre);
    }
}
