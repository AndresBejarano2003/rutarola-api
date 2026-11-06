using System.Net;
using System.Net.Http.Json;
using RutaRola.Application.Comun;
using RutaRola.Application.Empresas;
using RutaRola.Domain.Seguridad;
using RutaRola.Tests.Infraestructura;

namespace RutaRola.Tests.Seguridad;

[Collection(ColeccionBaseDatos.Nombre)]
public class PermisosTests(BaseDatosPrueba bd)
{
    [Theory]
    [InlineData(Roles.Conductor)]
    [InlineData(Roles.Monitora)]
    [InlineData(Roles.Acudiente)]
    public async Task Los_roles_operativos_no_entran_a_la_administracion(string rol)
    {
        var empresa = await bd.CrearEmpresaAsync();
        var cliente = await bd.ClienteDeAsync(await bd.CrearUsuarioPersonaAsync(empresa.Id, rol), "Clave-123");

        var respuesta = await cliente.GetAsync("/api/v1/configuracion");
        Assert.Equal("No tienes permisos para realizar esta acción.", await respuesta.MensajeErrorAsync(HttpStatusCode.Forbidden));
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync("/api/v1/empresas")).StatusCode);
    }

    [Fact]
    public async Task Sin_sesion_responde_401_y_el_admin_de_empresa_si_entra()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await bd.Cliente(null).GetAsync("/api/v1/configuracion")).StatusCode);

        var admin = await bd.ClienteDeAsync("admin", BaseDatosPrueba.ContrasenaAdmin);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/configuracion")).StatusCode);
    }

    [Fact]
    public async Task Solo_la_plataforma_gestiona_empresas()
    {
        var admin = await bd.ClienteDeAsync("admin", BaseDatosPrueba.ContrasenaAdmin);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/empresas")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.PostAsJsonAsync("/api/v1/empresas", new { nombre = "X", nit = "900111222-3", estado = "ACTIVO" }, ClienteApi.Json)).StatusCode);

        var superAdmin = await bd.SuperAdminAsync();
        var lista = await (await superAdmin.GetAsync("/api/v1/empresas?pagina=1&tamano=100")).LeerAsync<ListaPaginada<EmpresaDto>>();
        Assert.Contains(lista.Items, e => e.Nombre == "RutaRola Demo");
        Assert.Equal(1, lista.Pagina);

        // El SuperAdmin no pertenece a ninguna empresa.
        Assert.Equal(HttpStatusCode.NotFound, (await superAdmin.GetAsync("/api/v1/empresas/actual")).StatusCode);
    }

    [Fact]
    public async Task Cada_permiso_del_catalogo_es_una_policy_y_cada_rol_tiene_los_suyos()
    {
        var operativos = new[] { Roles.Conductor, Roles.Monitora };
        foreach (var rol in operativos)
        {
            Assert.DoesNotContain(Permisos.EstudiantesLeer, Permisos.PorRol[rol]);
        }
        Assert.DoesNotContain(Permisos.EmpresasGestionar, Permisos.PorRol[Roles.AdminEmpresa]);
        Assert.All(Permisos.PorRol.Values.SelectMany(p => p), p => Assert.Contains(p, Permisos.Todos));
    }
}
