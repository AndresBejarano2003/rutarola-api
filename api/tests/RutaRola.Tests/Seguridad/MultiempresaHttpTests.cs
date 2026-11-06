using System.Net;
using System.Net.Http.Json;
using RutaRola.Application.Comun;
using RutaRola.Application.Configuracion;
using RutaRola.Application.Empresas;
using RutaRola.Tests.Infraestructura;

namespace RutaRola.Tests.Seguridad;

/// <summary>Un usuario de la empresa A no puede leer ni modificar datos de la empresa B (por HTTP, con tokens reales).</summary>
[Collection(ColeccionBaseDatos.Nombre)]
public class MultiempresaHttpTests(BaseDatosPrueba bd)
{
    [Fact]
    public async Task La_configuracion_de_A_no_se_ve_ni_afecta_a_B()
    {
        var a = await bd.CrearEmpresaAsync();
        var b = await bd.CrearEmpresaAsync();
        var clienteA = await bd.ClienteDeAsync(a.UsuarioAdmin, a.ContrasenaAdmin);
        var clienteB = await bd.ClienteDeAsync(b.UsuarioAdmin, b.ContrasenaAdmin);

        var cambio = await clienteA.PutAsJsonAsync($"/api/v1/configuracion/{CatalogoConfiguracion.DiaLimitePago}", new { valor = "10" }, ClienteApi.Json);
        Assert.Equal("10", (await cambio.LeerAsync<ParametroDto>()).Valor);

        var deA = await (await clienteA.GetAsync("/api/v1/configuracion")).LeerAsync<List<ParametroDto>>();
        var deB = await (await clienteB.GetAsync("/api/v1/configuracion")).LeerAsync<List<ParametroDto>>();
        Assert.Equal("10", deA.Single(p => p.Clave == CatalogoConfiguracion.DiaLimitePago).Valor);
        Assert.Equal("5", deB.Single(p => p.Clave == CatalogoConfiguracion.DiaLimitePago).Valor);
    }

    [Fact]
    public async Task Cada_admin_ve_y_edita_solo_su_propia_empresa()
    {
        var a = await bd.CrearEmpresaAsync();
        var b = await bd.CrearEmpresaAsync();
        var clienteA = await bd.ClienteDeAsync(a.UsuarioAdmin, a.ContrasenaAdmin);
        var clienteB = await bd.ClienteDeAsync(b.UsuarioAdmin, b.ContrasenaAdmin);

        Assert.Equal(a.Id, (await (await clienteA.GetAsync("/api/v1/empresas/actual")).LeerAsync<EmpresaDto>()).Id);

        var editada = await clienteA.PutAsJsonAsync("/api/v1/empresas/actual", new { nombre = "A renombrada", ciudad = "Cali" }, ClienteApi.Json);
        Assert.Equal("A renombrada", (await editada.LeerAsync<EmpresaDto>()).Nombre);

        var empresaB = await (await clienteB.GetAsync("/api/v1/empresas/actual")).LeerAsync<EmpresaDto>();
        Assert.Equal(b.Id, empresaB.Id);
        Assert.NotEqual("A renombrada", empresaB.Nombre);
    }

    [Fact]
    public async Task A_no_puede_leer_ni_editar_la_empresa_B_por_su_id()
    {
        var a = await bd.CrearEmpresaAsync();
        var b = await bd.CrearEmpresaAsync();
        var clienteA = await bd.ClienteDeAsync(a.UsuarioAdmin, a.ContrasenaAdmin);

        Assert.Equal(HttpStatusCode.Forbidden, (await clienteA.GetAsync($"/api/v1/empresas/{b.Id}")).StatusCode);
        var edicion = await clienteA.PutAsJsonAsync($"/api/v1/empresas/{b.Id}",
            new { nombre = "Hackeada", nit = b.Nit, estado = "INACTIVO" }, ClienteApi.Json);
        Assert.Equal(HttpStatusCode.Forbidden, edicion.StatusCode);

        var superAdmin = await bd.SuperAdminAsync();
        var empresaB = await (await superAdmin.GetAsync($"/api/v1/empresas/{b.Id}")).LeerAsync<EmpresaDto>();
        Assert.Equal(EstadoRegistro.Activo, empresaB.Estado);
        Assert.NotEqual("Hackeada", empresaB.Nombre);
    }

    [Fact]
    public async Task La_plataforma_valida_NIT_y_usuario_unicos_al_crear_empresas()
    {
        var existente = await bd.CrearEmpresaAsync();
        var superAdmin = await bd.SuperAdminAsync();

        var nitRepetido = await superAdmin.PostAsJsonAsync("/api/v1/empresas",
            new { nombre = "Otra", nit = existente.Nit, estado = "ACTIVO" }, ClienteApi.Json);
        Assert.Contains(existente.Nit, await nitRepetido.MensajeErrorAsync(HttpStatusCode.Conflict));

        var usuarioRepetido = await superAdmin.PostAsJsonAsync("/api/v1/empresas", new
        {
            nombre = "Otra", nit = ClienteApi.NitUnico(), estado = "ACTIVO",
            administrador = new { nombre = "X", usuario = existente.UsuarioAdmin, contrasena = "Clave-123" },
        }, ClienteApi.Json);
        Assert.Equal(HttpStatusCode.Conflict, usuarioRepetido.StatusCode);

        var invalida = await superAdmin.PostAsJsonAsync("/api/v1/empresas", new { nombre = "", nit = "abc", estado = "OTRO" }, ClienteApi.Json);
        Assert.Equal("Ingresa el nombre de la empresa.", await invalida.MensajeErrorAsync(HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task La_busqueda_por_texto_ignora_tildes_y_mayusculas()
    {
        var nombre = $"Transportes Bogotá Ñandú {Guid.NewGuid():N}"[..40];
        await bd.CrearEmpresaAsync(nombre);
        var superAdmin = await bd.SuperAdminAsync();

        var lista = await (await superAdmin.GetAsync("/api/v1/empresas?texto=BOGOTA%20nandu")).LeerAsync<ListaPaginada<EmpresaDto>>();
        Assert.Contains(lista.Items, e => e.Nombre == nombre);
    }
}
