using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RutaRola.Application.Autenticacion;
using RutaRola.Application.Personas;
using RutaRola.Tests.Infraestructura;

namespace RutaRola.Tests.Seguridad;

/// <summary>Un acudiente con estudiantes en dos empresas une sus cuentas y cambia entre ellas con un solo usuario.</summary>
[Collection(ColeccionBaseDatos.Nombre)]
public class CuentasUnidasTests(BaseDatosPrueba bd)
{
    private record Familia(DatosCatalogos.Admin Admin, AcudienteDto Acudiente, EstudianteDto Estudiante, HttpClient Cliente);

    private async Task<Familia> FamiliaAsync()
    {
        var admin = await bd.AdminNuevoAsync();
        var (_, acudiente, estudiante) = await admin.Cliente.CrearEstudianteAsync();
        return new Familia(admin, acudiente, estudiante, await bd.ClienteDeAsync(acudiente.Usuario, "Clave-123"));
    }

    private static object Credenciales(string usuario, string contrasena = "Clave-123") => new { usuario, contrasena };

    private static async Task<IReadOnlyList<CuentaUnidaDto>> CuentasAsync(HttpClient cliente) =>
        await (await cliente.GetAsync("/api/v1/auth/cuentas")).LeerAsync<IReadOnlyList<CuentaUnidaDto>>();

    [Fact]
    public async Task Une_la_cuenta_de_otra_empresa_y_entra_a_ella_sin_contrasena_viendo_solo_los_datos_de_esa_empresa()
    {
        var a = await FamiliaAsync();
        var b = await FamiliaAsync();

        // Sin unir: solo la propia.
        var solo = await CuentasAsync(a.Cliente);
        Assert.True(Assert.Single(solo).Actual);

        Assert.Equal("Usuario o contraseña de la otra cuenta incorrectos.",
            await (await a.Cliente.PostAsJsonAsync("/api/v1/auth/cuentas", Credenciales(b.Acudiente.Usuario, "Otra-999"), ClienteApi.Json))
                .MensajeErrorAsync(HttpStatusCode.UnprocessableEntity));
        var unidas = await (await a.Cliente.PostAsJsonAsync("/api/v1/auth/cuentas", Credenciales(b.Acudiente.Usuario), ClienteApi.Json))
            .LeerAsync<IReadOnlyList<CuentaUnidaDto>>();
        Assert.Equal(2, unidas.Count);
        var otra = unidas.Single(c => !c.Actual);
        Assert.Equal(b.Admin.Empresa.Id, otra.IdEmpresa);
        Assert.True(otra.Disponible);
        Assert.Contains(b.Estudiante.PrimerNombre + " " + b.Estudiante.PrimerApellido, otra.Estudiantes);
        Assert.Equal(HttpStatusCode.Conflict,
            (await a.Cliente.PostAsJsonAsync("/api/v1/auth/cuentas", Credenciales(b.Acudiente.Usuario), ClienteApi.Json)).StatusCode);
        // Desde la otra cuenta también se ve el grupo.
        Assert.Equal(2, (await CuentasAsync(b.Cliente)).Count);

        // Entrar a la cuenta de la otra empresa: su sesión y SU portal; la de antes se cierra.
        var cambio = await (await a.Cliente.PostAsync($"/api/v1/auth/cuentas/{otra.IdUsuario}/sesion", null)).LeerAsync<RespuestaLogin>();
        Assert.Equal(b.Acudiente.Id, cambio.Sesion.AcudienteId);
        Assert.Equal(b.Admin.Empresa.Id, cambio.Sesion.Empresa!.Id);
        var enB = bd.Cliente(cambio.Tokens.AccessToken);
        var portal = JsonDocument.Parse(await (await enB.GetAsync("/api/v1/portal")).Content.ReadAsStringAsync()).RootElement;
        var ids = portal.GetProperty("estudiantes").EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();
        Assert.Equal([b.Estudiante.Id], ids);

        // Separar: cada una vuelve a estar sola.
        var separadas = await (await enB.DeleteAsync($"/api/v1/auth/cuentas/{otra.IdUsuario}")).LeerAsync<IReadOnlyList<CuentaUnidaDto>>();
        Assert.Single(separadas);
        Assert.Single(await CuentasAsync(await bd.ClienteDeAsync(a.Acudiente.Usuario, "Clave-123")));
    }

    [Fact]
    public async Task Solo_acudientes_de_empresas_distintas_que_lo_permitan()
    {
        var a = await FamiliaAsync();
        var b = await FamiliaAsync();
        var mismaEmpresa = await a.Admin.Cliente.CrearAcudienteAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await a.Admin.Cliente.GetAsync("/api/v1/auth/cuentas")).StatusCode);
        Assert.Contains("misma empresa", await (await a.Cliente.PostAsJsonAsync("/api/v1/auth/cuentas", Credenciales(mismaEmpresa.Usuario), ClienteApi.Json))
            .MensajeErrorAsync(HttpStatusCode.UnprocessableEntity));
        Assert.Contains("Solo se pueden unir cuentas de acudiente", await (await a.Cliente.PostAsJsonAsync(
            "/api/v1/auth/cuentas", Credenciales(b.Admin.Empresa.UsuarioAdmin, b.Admin.Empresa.ContrasenaAdmin), ClienteApi.Json))
            .MensajeErrorAsync(HttpStatusCode.UnprocessableEntity));

        // La empresa B lo desactiva: no se puede unir, y si ya estaban unidas no se puede entrar.
        (await b.Admin.Cliente.PutAsJsonAsync("/api/v1/configuracion/portal.acudientes_unen_cuentas", new { valor = "0" }, ClienteApi.Json)).EnsureSuccessStatusCode();
        Assert.Contains("no permite unir cuentas", await (await a.Cliente.PostAsJsonAsync("/api/v1/auth/cuentas", Credenciales(b.Acudiente.Usuario), ClienteApi.Json))
            .MensajeErrorAsync(HttpStatusCode.UnprocessableEntity));
        (await b.Admin.Cliente.PutAsJsonAsync("/api/v1/configuracion/portal.acudientes_unen_cuentas", new { valor = "1" }, ClienteApi.Json)).EnsureSuccessStatusCode();
        (await a.Cliente.PostAsJsonAsync("/api/v1/auth/cuentas", Credenciales(b.Acudiente.Usuario), ClienteApi.Json)).EnsureSuccessStatusCode();
        (await b.Admin.Cliente.PutAsJsonAsync("/api/v1/configuracion/portal.acudientes_unen_cuentas", new { valor = "0" }, ClienteApi.Json)).EnsureSuccessStatusCode();
        var otra = (await CuentasAsync(a.Cliente)).Single(c => !c.Actual);
        Assert.False(otra.Disponible);
        Assert.Contains("no permite unir cuentas", await (await a.Cliente.PostAsync($"/api/v1/auth/cuentas/{otra.IdUsuario}/sesion", null))
            .MensajeErrorAsync(HttpStatusCode.UnprocessableEntity));
    }
}
