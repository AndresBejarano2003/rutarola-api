using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RutaRola.Application.Autenticacion;
using RutaRola.Domain.Seguridad;
using RutaRola.Tests.Infraestructura;

namespace RutaRola.Tests.Seguridad;

[Collection(ColeccionBaseDatos.Nombre)]
public class AutenticacionTests(BaseDatosPrueba bd)
{
    [Theory]
    [InlineData("admin")]
    [InlineData("  ADMIN@rutarola.com ")]
    public async Task Login_con_usuario_o_correo_devuelve_la_sesion_del_frontend_y_los_tokens(string usuario)
    {
        var antes = DateTime.UtcNow.AddSeconds(-5);
        var login = await bd.LoginAsync(usuario, BaseDatosPrueba.ContrasenaAdmin);

        Assert.Equal("admin", login.Sesion.Usuario);
        Assert.Equal("ADMINISTRADOR", login.Sesion.Rol);
        Assert.Equal("admin@rutarola.com", login.Sesion.Correo);
        Assert.Null(login.Sesion.EmpleadoId);
        Assert.Equal("RutaRola Demo", login.Sesion.Empresa?.Nombre);
        Assert.Contains(Permisos.ConfiguracionGestionar, login.Sesion.Permisos);
        Assert.DoesNotContain(Permisos.EmpresasGestionar, login.Sesion.Permisos);
        Assert.InRange(login.Tokens.AccessTokenExpira, DateTime.UtcNow.AddMinutes(14), DateTime.UtcNow.AddMinutes(16));
        Assert.InRange(login.Tokens.RefreshTokenExpira, DateTime.UtcNow.AddDays(29), DateTime.UtcNow.AddDays(31));

        await using var db = bd.Contexto(null, modoSistema: true);
        var admin = await db.Usuarios.SingleAsync(u => u.NombreUsuario == "admin");
        Assert.True(admin.UltimoLogin >= antes);

        // Del refresh token solo se guarda el hash (SHA-256), nunca el token.
        var hash = bd.Api.Services.GetRequiredService<IGeneradorTokens>().Hash(login.Tokens.RefreshToken);
        Assert.False(await db.SesionesUsuario.AnyAsync(s => s.TokenHash == login.Tokens.RefreshToken));
        var sesion = await db.SesionesUsuario.SingleAsync(s => s.TokenHash == hash);
        Assert.Equal(admin.Id, sesion.IdUsuario);
        Assert.False(sesion.Revocado);
    }

    [Theory]
    [InlineData("admin", "clave-equivocada")]
    [InlineData("no-existe", "cualquiera")]
    public async Task Credenciales_invalidas_responden_401_sin_revelar_que_fallo(string usuario, string contrasena)
    {
        var respuesta = await bd.PostLoginAsync(usuario, contrasena);
        Assert.Equal(ServicioAutenticacion.MensajeCredencialesInvalidas, await respuesta.MensajeErrorAsync(HttpStatusCode.Unauthorized));
    }

    [Fact]
    public async Task Login_sin_datos_responde_400_con_el_mensaje_del_frontend()
    {
        var respuesta = await bd.PostLoginAsync("", "");
        Assert.Equal("Ingresa tu usuario y tu contraseña.", await respuesta.MensajeErrorAsync(HttpStatusCode.BadRequest));
    }

    [Theory]
    [InlineData(Roles.Conductor, "CONDUCTOR")]
    [InlineData(Roles.Monitora, "MONITORA")]
    [InlineData(Roles.Acudiente, "ACUDIENTE")]
    public async Task Cada_rol_recibe_su_codigo_y_la_persona_vinculada(string rol, string codigo)
    {
        var empresa = await bd.CrearEmpresaAsync();
        var usuario = await bd.CrearUsuarioPersonaAsync(empresa.Id, rol);

        var sesion = (await bd.LoginAsync(usuario, "Clave-123")).Sesion;

        Assert.Equal(codigo, sesion.Rol);
        Assert.Equal(empresa.Id, sesion.Empresa?.Id);
        if (rol == Roles.Acudiente)
        {
            Assert.NotNull(sesion.AcudienteId);
            Assert.Equal("Ana Pérez", sesion.Nombre);
            Assert.Equal([Permisos.GpsConsultar, Permisos.NotificacionesPropias, Permisos.PortalConsultar], sesion.Permisos);
        }
        else
        {
            Assert.NotNull(sesion.EmpleadoId);
            Assert.Equal("Juan Cárdenas", sesion.Nombre);
            Assert.Contains(Permisos.RecorridosOperar, sesion.Permisos);
        }
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Usuario_o_persona_inactivos_no_entran(bool usuarioActivo, bool personaActiva)
    {
        var empresa = await bd.CrearEmpresaAsync();
        var usuario = await bd.CrearUsuarioPersonaAsync(empresa.Id, Roles.Conductor, usuarioActivo, personaActiva);

        var respuesta = await bd.PostLoginAsync(usuario, "Clave-123");
        Assert.Equal(ServicioAutenticacion.MensajeInactivo, await respuesta.MensajeErrorAsync(HttpStatusCode.Forbidden));
    }

    [Fact]
    public async Task Una_empresa_inactiva_bloquea_el_ingreso_de_sus_usuarios()
    {
        var empresa = await bd.CrearEmpresaAsync();
        await using (var db = bd.Contexto(null, modoSistema: true))
        {
            (await db.Empresas.SingleAsync(e => e.Id == empresa.Id)).Activo = false;
            await db.SaveChangesAsync();
        }

        var respuesta = await bd.PostLoginAsync(empresa.UsuarioAdmin, empresa.ContrasenaAdmin);
        Assert.Contains("empresa", await respuesta.MensajeErrorAsync(HttpStatusCode.Forbidden));
    }

    [Fact]
    public async Task Refresh_entrega_un_par_nuevo_y_el_anterior_deja_de_servir()
    {
        var empresa = await bd.CrearEmpresaAsync();
        var login = await bd.LoginAsync(empresa.UsuarioAdmin, empresa.ContrasenaAdmin);
        var cliente = bd.Api.CreateClient();

        var nuevos = await (await cliente.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = login.Tokens.RefreshToken }, ClienteApi.Json))
            .LeerAsync<TokensDto>();
        Assert.NotEqual(login.Tokens.RefreshToken, nuevos.RefreshToken);

        // El access token nuevo sirve.
        var me = await bd.Cliente(nuevos.AccessToken).GetAsync("/api/v1/auth/me");
        Assert.Equal(empresa.UsuarioAdmin, (await me.LeerAsync<SesionDto>()).Usuario);

        // Reusar el refresh rotado → 401 y, por ser posible robo, se revocan TODAS las sesiones del usuario.
        var reuso = await cliente.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = login.Tokens.RefreshToken }, ClienteApi.Json);
        Assert.Equal(ServicioAutenticacion.MensajeSesionInvalida, await reuso.MensajeErrorAsync(HttpStatusCode.Unauthorized));
        var conElNuevo = await cliente.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = nuevos.RefreshToken }, ClienteApi.Json);
        Assert.Equal(HttpStatusCode.Unauthorized, conElNuevo.StatusCode);
    }

    [Fact]
    public async Task Refresh_desconocido_o_vacio_es_rechazado()
    {
        var cliente = bd.Api.CreateClient();
        var desconocido = await cliente.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = "no-existe" }, ClienteApi.Json);
        Assert.Equal(HttpStatusCode.Unauthorized, desconocido.StatusCode);
        var vacio = await cliente.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = "" }, ClienteApi.Json);
        Assert.Equal(HttpStatusCode.BadRequest, vacio.StatusCode);
    }

    [Fact]
    public async Task Logout_revoca_el_refresh_token()
    {
        var empresa = await bd.CrearEmpresaAsync();
        var login = await bd.LoginAsync(empresa.UsuarioAdmin, empresa.ContrasenaAdmin);
        var cliente = bd.Cliente(login.Tokens.AccessToken);

        var salida = await cliente.PostAsJsonAsync("/api/v1/auth/logout", new { refreshToken = login.Tokens.RefreshToken }, ClienteApi.Json);
        Assert.Equal(HttpStatusCode.NoContent, salida.StatusCode);

        var refresh = await cliente.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = login.Tokens.RefreshToken }, ClienteApi.Json);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);

        // Logout es idempotente y funciona sin cuerpo.
        Assert.Equal(HttpStatusCode.NoContent, (await bd.Api.CreateClient().PostAsync("/api/v1/auth/logout", null)).StatusCode);
    }

    [Fact]
    public async Task Me_exige_un_token_valido_y_firmado_por_el_api()
    {
        var login = await bd.LoginAsync("admin", BaseDatosPrueba.ContrasenaAdmin);

        Assert.Equal(HttpStatusCode.Unauthorized, (await bd.Cliente(null).GetAsync("/api/v1/auth/me")).StatusCode);

        // Token alterado (p. ej. cambiar la empresa en el payload) → firma inválida.
        var partes = login.Tokens.AccessToken.Split('.');
        var alterado = $"{partes[0]}.{partes[1][..^2]}AA.{partes[2]}";
        var respuesta = await bd.Cliente(alterado).GetAsync("/api/v1/auth/me");
        Assert.Equal(ServicioAutenticacion.MensajeSesionInvalida, await respuesta.MensajeErrorAsync(HttpStatusCode.Unauthorized));
        Assert.Contains("invalid_token", respuesta.Headers.WwwAuthenticate.ToString());

        var me = await (await bd.Cliente(login.Tokens.AccessToken).GetAsync("/api/v1/auth/me")).LeerAsync<SesionDto>();
        Assert.Equal("admin", me.Usuario);
        Assert.Equal(login.Sesion.Permisos, me.Permisos);
    }
}
