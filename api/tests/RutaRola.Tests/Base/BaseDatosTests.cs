using Microsoft.EntityFrameworkCore;
using RutaRola.Domain.Seguridad;
using RutaRola.Infrastructure.Persistencia;
using RutaRola.Tests.Infraestructura;

namespace RutaRola.Tests.Base;

[Collection(ColeccionBaseDatos.Nombre)]
public class BaseDatosTests(BaseDatosPrueba bd)
{
    [Fact]
    public async Task Migrar_y_sembrar_es_idempotente()
    {
        await BaseDatos.MigrarYSembrarAsync(bd.Api.Services);

        await using var db = bd.Contexto(null, modoSistema: true);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(Permisos.Todos.Count(), await db.Permisos.CountAsync());
        Assert.Equal(5, await db.Roles.CountAsync());
        Assert.Equal(1, await db.Usuarios.CountAsync(u => u.NombreUsuario == "admin"));
        Assert.Equal(Permisos.PorRol[Roles.AdminEmpresa].Count,
            await db.RolesPermisos.CountAsync(rp => rp.Rol.Nombre == Roles.AdminEmpresa));
    }

    [Fact]
    public async Task El_admin_demo_queda_con_la_contrasena_hasheada_de_la_configuracion()
    {
        await using var db = bd.Contexto(null, modoSistema: true);
        var admin = await db.Usuarios.Include(u => u.Rol).SingleAsync(u => u.NombreUsuario == "admin");

        Assert.Equal(Roles.AdminEmpresa, admin.Rol.Nombre);
        Assert.NotNull(admin.IdEmpresa);
        Assert.DoesNotContain(BaseDatosPrueba.ContrasenaAdmin, admin.PasswordHash);
        var hasher = new Microsoft.AspNetCore.Identity.PasswordHasher<Usuario>();
        Assert.NotEqual(Microsoft.AspNetCore.Identity.PasswordVerificationResult.Failed,
            hasher.VerifyHashedPassword(admin, admin.PasswordHash, BaseDatosPrueba.ContrasenaAdmin));
    }

    [Fact]
    public async Task Sin_contrasena_de_seed_no_se_crea_el_superadmin()
    {
        await using var db = bd.Contexto(null, modoSistema: true);
        Assert.False(await db.Usuarios.AnyAsync(u => u.NombreUsuario == "superadmin"));
    }
}
