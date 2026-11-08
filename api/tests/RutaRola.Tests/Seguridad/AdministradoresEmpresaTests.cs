using System.Net;
using System.Net.Http.Json;
using RutaRola.Application.Empresas;
using RutaRola.Tests.Infraestructura;

namespace RutaRola.Tests.Seguridad;

/// <summary>El SuperAdmin ve y gestiona los administradores de cada empresa (incluida su contraseña).</summary>
[Collection(ColeccionBaseDatos.Nombre)]
public class AdministradoresEmpresaTests(BaseDatosPrueba bd)
{
    [Fact]
    public async Task El_superadmin_lista_los_administradores_y_restablece_la_contrasena()
    {
        var empresa = await bd.CrearEmpresaAsync();
        var sesionVieja = await bd.LoginAsync(empresa.UsuarioAdmin, empresa.ContrasenaAdmin);
        var super = await bd.SuperAdminAsync();
        var url = $"/api/v1/empresas/{empresa.Id}/administradores";

        var lista = await (await super.GetAsync(url)).LeerAsync<List<AdministradorEmpresaDto>>();
        var admin = Assert.Single(lista);
        Assert.Equal((empresa.UsuarioAdmin, "ACTIVO"), (admin.Usuario, admin.Estado));
        Assert.NotNull(admin.UltimoLogin);
        Assert.DoesNotContain("contrasena", await (await super.GetAsync(url)).Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.BadRequest, (await super.PutAsJsonAsync($"{url}/{admin.Id}/contrasena", new { nueva = "123" }, ClienteApi.Json)).StatusCode);
        await (await super.PutAsJsonAsync($"{url}/{admin.Id}/contrasena", new { nueva = "Restablecida-1" }, ClienteApi.Json)).LeerAsync(HttpStatusCode.NoContent);

        // La contraseña anterior ya no sirve y sus sesiones se cerraron.
        Assert.Equal(HttpStatusCode.Unauthorized, (await bd.PostLoginAsync(empresa.UsuarioAdmin, empresa.ContrasenaAdmin)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await bd.Api.CreateClient().PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = sesionVieja.Tokens.RefreshToken }, ClienteApi.Json)).StatusCode);
        await bd.LoginAsync(empresa.UsuarioAdmin, "Restablecida-1");
    }

    [Fact]
    public async Task Crea_otro_administrador_lo_edita_e_inactiva_y_valida_el_usuario_unico()
    {
        var empresa = await bd.CrearEmpresaAsync();
        var super = await bd.SuperAdminAsync();
        var url = $"/api/v1/empresas/{empresa.Id}/administradores";
        var usuario = ClienteApi.Unico("coord");

        Assert.Equal("Ingresa la contraseña.", await (await super.PostAsJsonAsync(url,
            new { nombre = "Coordinadora", usuario, estado = "ACTIVO", contrasena = "" }, ClienteApi.Json)).MensajeErrorAsync(HttpStatusCode.UnprocessableEntity));
        Assert.Equal($"El usuario \"{empresa.UsuarioAdmin}\" ya está en uso. Elige otro.", await (await super.PostAsJsonAsync(url,
            new { nombre = "Otro", usuario = empresa.UsuarioAdmin, estado = "ACTIVO", contrasena = "Clave-123" }, ClienteApi.Json)).MensajeErrorAsync(HttpStatusCode.Conflict));

        var nuevo = await (await super.PostAsJsonAsync(url,
            new { nombre = "Coordinadora", usuario = usuario.ToUpperInvariant(), email = "Coord@Empresa.co", estado = "ACTIVO", contrasena = "Clave-123" }, ClienteApi.Json))
            .LeerAsync<AdministradorEmpresaDto>(HttpStatusCode.Created);
        Assert.Equal((usuario, "coord@empresa.co", ""), (nuevo.Usuario, nuevo.Email, nuevo.Telefono));
        var cliente = await bd.ClienteDeAsync(usuario, "Clave-123");
        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/api/v1/estudiantes")).StatusCode);

        // Editar sin contraseña la conserva; inactivar impide entrar.
        var editado = await (await super.PutAsJsonAsync($"{url}/{nuevo.Id}",
            new { nombre = "Coordinadora general", usuario, email = "", telefono = "3001112233", estado = "INACTIVO", contrasena = "" }, ClienteApi.Json))
            .LeerAsync<AdministradorEmpresaDto>();
        Assert.Equal(("Coordinadora general", "INACTIVO"), (editado.Nombre, editado.Estado));
        Assert.Equal(2, (await (await super.GetAsync(url)).LeerAsync<List<AdministradorEmpresaDto>>()).Count);
        Assert.Equal(HttpStatusCode.Forbidden, (await bd.PostLoginAsync(usuario, "Clave-123")).StatusCode);
    }

    [Fact]
    public async Task Solo_la_plataforma_y_solo_administradores_de_esa_empresa()
    {
        var a = await bd.CrearEmpresaAsync();
        var b = await bd.CrearEmpresaAsync();
        var super = await bd.SuperAdminAsync();
        var adminA = Assert.Single(await (await super.GetAsync($"/api/v1/empresas/{a.Id}/administradores")).LeerAsync<List<AdministradorEmpresaDto>>());

        // El administrador de una empresa no usa estos endpoints (ni para la suya).
        var clienteA = await bd.ClienteDeAsync(a.UsuarioAdmin, a.ContrasenaAdmin);
        Assert.Equal(HttpStatusCode.Forbidden, (await clienteA.GetAsync($"/api/v1/empresas/{a.Id}/administradores")).StatusCode);
        // Un administrador de A no se toca a través de B; ni conductores o acudientes como si fueran administradores.
        Assert.Equal(HttpStatusCode.NotFound, (await super.PutAsJsonAsync($"/api/v1/empresas/{b.Id}/administradores/{adminA.Id}/contrasena",
            new { nueva = "Otra-1234" }, ClienteApi.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await super.GetAsync($"/api/v1/empresas/{Guid.NewGuid()}/administradores")).StatusCode);
        await bd.LoginAsync(a.UsuarioAdmin, a.ContrasenaAdmin);
    }
}
