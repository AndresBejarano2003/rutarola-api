using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using RutaRola.Api.Configuracion;
using RutaRola.Application.Autenticacion;
using RutaRola.Application.Empresas;
using RutaRola.Domain.Enums;
using RutaRola.Domain.Personas;
using RutaRola.Domain.Seguridad;
using Microsoft.EntityFrameworkCore;

namespace RutaRola.Tests.Infraestructura;

/// <summary>Ayudas para las pruebas por HTTP: login, clientes autenticados y datos de prueba.</summary>
public static class ClienteApi
{
    public static readonly JsonSerializerOptions Json = Crear();

    private static JsonSerializerOptions Crear()
    {
        var opciones = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Servicios.ConfigurarJson(opciones);
        return opciones;
    }

    public static async Task<HttpResponseMessage> PostLoginAsync(this BaseDatosPrueba bd, string usuario, string contrasena) =>
        await bd.Api.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { usuario, contrasena }, Json);

    public static async Task<RespuestaLogin> LoginAsync(this BaseDatosPrueba bd, string usuario, string contrasena)
    {
        var respuesta = await bd.PostLoginAsync(usuario, contrasena);
        Assert.True(respuesta.StatusCode == HttpStatusCode.OK, $"Login de {usuario}: {(int)respuesta.StatusCode} {await respuesta.Content.ReadAsStringAsync()}");
        return (await respuesta.Content.ReadFromJsonAsync<RespuestaLogin>(Json))!;
    }

    public static HttpClient Cliente(this BaseDatosPrueba bd, string? accessToken)
    {
        var cliente = bd.Api.CreateClient();
        if (accessToken is not null)
        {
            cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }
        return cliente;
    }

    public static async Task<HttpClient> ClienteDeAsync(this BaseDatosPrueba bd, string usuario, string contrasena) =>
        bd.Cliente((await bd.LoginAsync(usuario, contrasena)).Tokens.AccessToken);

    public static Task<HttpClient> SuperAdminAsync(this BaseDatosPrueba bd) => bd.ClienteDeAsync("superadmin", BaseDatosPrueba.ContrasenaSuperAdmin);

    public static async Task<T> LeerAsync<T>(this HttpResponseMessage respuesta, HttpStatusCode esperado = HttpStatusCode.OK)
    {
        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        Assert.True(respuesta.StatusCode == esperado, $"Se esperaba {(int)esperado} y llegó {(int)respuesta.StatusCode}: {cuerpo}");
        return JsonSerializer.Deserialize<T>(cuerpo, Json)!;
    }

    /// <summary>Solo verifica el código (respuestas sin cuerpo, como 204).</summary>
    public static async Task LeerAsync(this HttpResponseMessage respuesta, HttpStatusCode esperado)
    {
        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        Assert.True(respuesta.StatusCode == esperado, $"Se esperaba {(int)esperado} y llegó {(int)respuesta.StatusCode}: {cuerpo}");
    }

    /// <summary>Verifica un ProblemDetails y devuelve su <c>mensaje</c>.</summary>
    public static async Task<string> MensajeErrorAsync(this HttpResponseMessage respuesta, HttpStatusCode esperado)
    {
        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        Assert.True(respuesta.StatusCode == esperado, $"Se esperaba {(int)esperado} y llegó {(int)respuesta.StatusCode}: {cuerpo}");
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        return JsonDocument.Parse(cuerpo).RootElement.GetProperty("mensaje").GetString()!;
    }

    public record EmpresaPrueba(Guid Id, string Nit, string UsuarioAdmin, string ContrasenaAdmin);

    private static int consecutivo;

    public static string NitUnico() => $"8{Random.Shared.Next(10000000, 99999999)}{Interlocked.Increment(ref consecutivo) % 10}-1";

    public static string Unico(string prefijo) => $"{prefijo}{Guid.NewGuid():N}"[..20];

    /// <summary>Crea una empresa con su admin a través del API (como lo haría la plataforma).</summary>
    public static async Task<EmpresaPrueba> CrearEmpresaAsync(this BaseDatosPrueba bd, string? nombre = null)
    {
        var superAdmin = await bd.SuperAdminAsync();
        var usuario = Unico("admin");
        var nit = NitUnico();
        var respuesta = await superAdmin.PostAsJsonAsync("/api/v1/empresas", new
        {
            nombre = nombre ?? $"Transportes {usuario}",
            nit,
            estado = "ACTIVO",
            administrador = new { nombre = "Admin de prueba", usuario, contrasena = "Clave-123" },
        }, Json);
        var empresa = await respuesta.LeerAsync<EmpresaDto>(HttpStatusCode.Created);
        return new EmpresaPrueba(empresa.Id, nit, usuario, "Clave-123");
    }

    /// <summary>Crea directamente en la BD un usuario operativo (conductor, monitora o acudiente) con su persona vinculada.</summary>
    public static async Task<string> CrearUsuarioPersonaAsync(this BaseDatosPrueba bd, Guid idEmpresa, string rol,
        bool usuarioActivo = true, bool personaActiva = true, string contrasena = "Clave-123")
    {
        await using var db = bd.Contexto(null, modoSistema: true);
        var idRol = await db.Roles.Where(r => r.Nombre == rol).Select(r => r.Id).SingleAsync();
        var nombreUsuario = Unico(rol.ToLowerInvariant());
        var usuario = new Usuario { IdEmpresa = idEmpresa, IdRol = idRol, Nombre = nombreUsuario, NombreUsuario = nombreUsuario, Activo = usuarioActivo };
        usuario.PasswordHash = new PasswordHasher<Usuario>().HashPassword(usuario, contrasena);
        db.Usuarios.Add(usuario);

        var documento = Random.Shared.Next(10000000, 99999999).ToString();
        if (rol == Roles.Acudiente)
        {
            db.Acudientes.Add(new Acudiente
            {
                IdEmpresa = idEmpresa, IdUsuario = usuario.Id, TipoDocumento = "CC", NumeroDocumento = documento,
                PrimerNombre = "Ana", PrimerApellido = "Pérez", Telefono = "3001234567", Correo = "ana@correo.com", Activo = personaActiva,
            });
        }
        else if (rol is Roles.Conductor or Roles.Monitora)
        {
            db.Empleados.Add(new Empleado
            {
                IdEmpresa = idEmpresa, IdUsuario = usuario.Id, TipoDocumento = "CC", NumeroDocumento = documento,
                PrimerNombre = "Juan", PrimerApellido = "Cárdenas", Telefono = "3109876543",
                TipoEmpleado = rol == Roles.Conductor ? TipoEmpleado.Conductor : TipoEmpleado.Monitora,
                FechaIngreso = new DateOnly(2025, 1, 15), Sueldo = 1_800_000, TipoContrato = TipoContrato.TerminoFijo, Activo = personaActiva,
            });
        }
        await db.SaveChangesAsync();
        return nombreUsuario;
    }
}
