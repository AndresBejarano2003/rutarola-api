using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using RutaRola.Domain.Enums;
using RutaRola.Tests.Infraestructura;

namespace RutaRola.Tests.Seguridad;

[Collection(ColeccionBaseDatos.Nombre)]
public class CambioContrasenaTests(BaseDatosPrueba bd)
{
    [Fact]
    public async Task Cambia_la_contrasena_propia_y_cierra_las_demas_sesiones()
    {
        var empresa = await bd.CrearEmpresaAsync();
        var otra = await bd.LoginAsync(empresa.UsuarioAdmin, empresa.ContrasenaAdmin);
        var actual = await bd.LoginAsync(empresa.UsuarioAdmin, empresa.ContrasenaAdmin);
        var cliente = bd.Cliente(actual.Tokens.AccessToken);

        Assert.Equal("La contraseña actual no es correcta.", await (await cliente.PutAsJsonAsync("/api/v1/auth/contrasena",
            new { actual = "equivocada", nueva = "Nueva-123" }, ClienteApi.Json)).MensajeErrorAsync(HttpStatusCode.UnprocessableEntity));
        Assert.Equal("La nueva contraseña debe tener al menos 6 caracteres.", await (await cliente.PutAsJsonAsync("/api/v1/auth/contrasena",
            new { actual = empresa.ContrasenaAdmin, nueva = "123" }, ClienteApi.Json)).MensajeErrorAsync(HttpStatusCode.BadRequest));

        await (await cliente.PutAsJsonAsync("/api/v1/auth/contrasena",
            new { actual = empresa.ContrasenaAdmin, nueva = "Nueva-123" }, ClienteApi.Json)).LeerAsync(HttpStatusCode.NoContent);

        Assert.Equal(HttpStatusCode.Unauthorized, (await bd.PostLoginAsync(empresa.UsuarioAdmin, empresa.ContrasenaAdmin)).StatusCode);
        await bd.LoginAsync(empresa.UsuarioAdmin, "Nueva-123");
        // La otra sesión ya no se renueva; la actual sí.
        Assert.Equal(HttpStatusCode.Unauthorized, (await bd.Api.CreateClient().PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = otra.Tokens.RefreshToken }, ClienteApi.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bd.Api.CreateClient().PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = actual.Tokens.RefreshToken }, ClienteApi.Json)).StatusCode);

        // La auditoría no guarda el hash.
        await using var db = bd.Contexto(null, modoSistema: true);
        var usuario = await db.Usuarios.SingleAsync(u => u.NombreUsuario == empresa.UsuarioAdmin);
        var cambio = await db.Auditoria.Where(a => a.IdRegistro == usuario.Id && a.Accion == AccionAuditoria.Actualizar)
            .OrderByDescending(a => a.Id).FirstAsync();
        Assert.Contains("(cambiada)", cambio.ValorNuevo);
        Assert.DoesNotContain(usuario.PasswordHash, cambio.ValorNuevo);
    }

    [Fact]
    public async Task Exige_sesion()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await bd.Api.CreateClient().PutAsJsonAsync("/api/v1/auth/contrasena",
            new { actual = "x", nueva = "Nueva-123" }, ClienteApi.Json)).StatusCode);
    }
}
