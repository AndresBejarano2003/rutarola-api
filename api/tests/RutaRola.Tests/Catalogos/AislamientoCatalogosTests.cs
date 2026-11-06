using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Application.Operacion;
using RutaRola.Application.Personas;
using RutaRola.Domain.Enums;
using RutaRola.Domain.Seguridad;
using RutaRola.Tests.Infraestructura;

namespace RutaRola.Tests.Catalogos;

/// <summary>Multiempresa, permisos y auditoría de los catálogos.</summary>
[Collection(ColeccionBaseDatos.Nombre)]
public class AislamientoCatalogosTests(BaseDatosPrueba bd)
{
    public static TheoryData<string> Recursos => ["colegios", "vehiculos", "empleados", "acudientes", "estudiantes"];

    private static async Task<Guid> CrearAsync(HttpClient cliente, string recurso) => recurso switch
    {
        "colegios" => (await cliente.CrearColegioAsync()).Id,
        "vehiculos" => (await cliente.CrearAsync<VehiculoDto>("vehiculos", DatosCatalogos.Vehiculo("ABC123", "1"))).Id,
        "empleados" => (await cliente.CrearAsync<EmpleadoDto>("empleados", DatosCatalogos.Empleado(ClienteApi.Unico("e")))).Id,
        "acudientes" => (await cliente.CrearAcudienteAsync()).Id,
        _ => (await cliente.CrearEstudianteAsync()).Estudiante.Id,
    };

    [Theory]
    [MemberData(nameof(Recursos))]
    public async Task A_no_ve_ni_modifica_los_registros_de_B(string recurso)
    {
        var a = await bd.AdminNuevoAsync();
        var b = await bd.AdminNuevoAsync();
        var idDeB = await CrearAsync(b.Cliente, recurso);

        var lista = await (await a.Cliente.GetAsync($"/api/v1/{recurso}")).Content.ReadAsStringAsync();
        Assert.DoesNotContain(idDeB.ToString(), lista);
        Assert.DoesNotContain(idDeB.ToString(), await (await a.Cliente.GetAsync($"/api/v1/{recurso}/opciones")).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await a.Cliente.GetAsync($"/api/v1/{recurso}/{idDeB}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.Cliente.GetAsync($"/api/v1/{recurso}/{idDeB}/motivo-no-eliminar")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.Cliente.DeleteAsync($"/api/v1/{recurso}/{idDeB}")).StatusCode);

        // B sigue viendo su registro intacto.
        Assert.Equal(HttpStatusCode.OK, (await b.Cliente.GetAsync($"/api/v1/{recurso}/{idDeB}")).StatusCode);
    }

    [Fact]
    public async Task A_no_puede_editar_un_colegio_de_B_ni_usarlo_en_sus_estudiantes()
    {
        var a = await bd.AdminNuevoAsync();
        var b = await bd.AdminNuevoAsync();
        var colegioB = await b.Cliente.CrearColegioAsync();
        var acudienteB = await b.Cliente.CrearAcudienteAsync();
        var acudienteA = await a.Cliente.CrearAcudienteAsync();
        var colegioA = await a.Cliente.CrearColegioAsync();

        var edicion = await a.Cliente.PutAsJsonAsync($"/api/v1/colegios/{colegioB.Id}", DatosCatalogos.Colegio(razonSocial: "Hackeado"), ClienteApi.Json);
        Assert.Equal(HttpStatusCode.NotFound, edicion.StatusCode);

        var conColegioB = await a.Cliente.PostAsJsonAsync("/api/v1/estudiantes", DatosCatalogos.Estudiante(acudienteA.Id, colegioB.Id), ClienteApi.Json);
        Assert.Equal("El colegio no existe o fue eliminado.", await conColegioB.MensajeErrorAsync(HttpStatusCode.NotFound));
        var conAcudienteB = await a.Cliente.PostAsJsonAsync("/api/v1/estudiantes", DatosCatalogos.Estudiante(acudienteB.Id, colegioA.Id), ClienteApi.Json);
        Assert.Equal("El acudiente no existe o fue eliminado.", await conAcudienteB.MensajeErrorAsync(HttpStatusCode.NotFound));

        Assert.Equal("Colegio San Cayetano", (await (await b.Cliente.GetAsync($"/api/v1/colegios/{colegioB.Id}")).LeerAsync<ColegioDto>()).RazonSocial);
    }

    [Fact]
    public async Task La_empresa_del_registro_sale_del_token_y_no_del_body()
    {
        var a = await bd.AdminNuevoAsync();
        var b = await bd.AdminNuevoAsync();
        var solicitud = (Dictionary<string, object?>)DatosCatalogos.Acudiente(ClienteApi.Unico("a"));
        solicitud["idEmpresa"] = b.Empresa.Id;

        var creado = await a.Cliente.CrearAsync<AcudienteDto>("acudientes", solicitud);

        await using var db = bd.Contexto(null, modoSistema: true);
        Assert.Equal(a.Empresa.Id, (await db.Acudientes.SingleAsync(x => x.Id == creado.Id)).IdEmpresa);
    }

    [Theory]
    [InlineData(Roles.Conductor)]
    [InlineData(Roles.Monitora)]
    [InlineData(Roles.Acudiente)]
    public async Task Los_usuarios_operativos_no_acceden_a_los_catalogos(string rol)
    {
        var admin = await bd.AdminNuevoAsync();
        var usuario = await bd.CrearUsuarioPersonaAsync(admin.Empresa.Id, rol);
        var cliente = await bd.ClienteDeAsync(usuario, "Clave-123");

        foreach (var recurso in new[] { "colegios", "vehiculos", "empleados", "acudientes", "estudiantes", "conductores" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync($"/api/v1/{recurso}")).StatusCode);
        }
        var crear = await cliente.PostAsJsonAsync("/api/v1/colegios", DatosCatalogos.Colegio(), ClienteApi.Json);
        Assert.Equal(HttpStatusCode.Forbidden, crear.StatusCode);
    }

    [Fact]
    public async Task Sin_sesion_responde_401()
    {
        var anonimo = bd.Cliente(null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/v1/estudiantes")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync($"/api/v1/estudiantes/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Crear_editar_y_eliminar_un_estudiante_queda_en_la_auditoria()
    {
        var admin = await bd.AdminNuevoAsync();
        var (colegio, acudiente, estudiante) = await admin.Cliente.CrearEstudianteAsync();
        var cambio = DatosCatalogos.Estudiante(acudiente.Id, colegio.Id, documento: estudiante.NumeroDocumento, primerNombre: "Pedro");
        await (await admin.Cliente.PutAsJsonAsync($"/api/v1/estudiantes/{estudiante.Id}", cambio, ClienteApi.Json)).LeerAsync<EstudianteDto>();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Cliente.DeleteAsync($"/api/v1/estudiantes/{estudiante.Id}")).StatusCode);

        await using var db = bd.Contexto(null, modoSistema: true);
        var registros = await db.Auditoria.Where(x => x.TablaAfectada == "estudiantes" && x.IdRegistro == estudiante.Id)
            .OrderBy(x => x.Id).ToListAsync();
        Assert.Equal([AccionAuditoria.Crear, AccionAuditoria.Actualizar, AccionAuditoria.Eliminar], registros.Select(x => x.Accion));
        Assert.All(registros, x => Assert.Equal(admin.Empresa.Id, x.IdEmpresa));
        var idAdmin = await db.Usuarios.Where(u => u.NombreUsuario == admin.Empresa.UsuarioAdmin).Select(u => u.Id).SingleAsync();
        Assert.All(registros, x => Assert.Equal(idAdmin, x.IdUsuario));
        Assert.Contains("\"primerNombre\": \"Juan\"", registros[1].ValorAnterior!.Replace("\":\"", "\": \""));
        Assert.Contains("Pedro", registros[1].ValorNuevo);
        Assert.DoesNotContain("tipoDocumento", registros[1].ValorNuevo); // solo lo que cambió
        Assert.Null(registros[2].ValorNuevo);
    }

    [Fact]
    public async Task La_auditoria_de_usuarios_nunca_guarda_la_contrasena()
    {
        var admin = await bd.AdminNuevoAsync();
        var usuario = ClienteApi.Unico("a");
        var acudiente = await admin.Cliente.CrearAsync<AcudienteDto>("acudientes", DatosCatalogos.Acudiente(usuario));
        var cambio = DatosCatalogos.Acudiente(usuario, acudiente.NumeroDocumento, contrasena: "Otra-Clave-9");
        await (await admin.Cliente.PutAsJsonAsync($"/api/v1/acudientes/{acudiente.Id}", cambio, ClienteApi.Json)).LeerAsync<AcudienteDto>();

        await using var db = bd.Contexto(null, modoSistema: true);
        var idUsuario = await db.Usuarios.Where(u => u.NombreUsuario == usuario).Select(u => u.Id).SingleAsync();
        var registros = await db.Auditoria.Where(x => x.TablaAfectada == "usuarios" && x.IdRegistro == idUsuario).OrderBy(x => x.Id).ToListAsync();

        Assert.Equal([AccionAuditoria.Crear, AccionAuditoria.Actualizar], registros.Select(x => x.Accion));
        Assert.All(registros, x => Assert.DoesNotContain("passwordHash", (x.ValorAnterior ?? "") + x.ValorNuevo, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("(cambiada)", registros[1].ValorNuevo);
        Assert.True(await db.Auditoria.AnyAsync(x => x.TablaAfectada == "acudientes" && x.IdRegistro == acudiente.Id));
    }

    [Fact]
    public async Task La_vista_de_conductores_muestra_el_estado_de_la_licencia()
    {
        var admin = await bd.AdminNuevoAsync();
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-5));
        await admin.Cliente.CrearAsync<EmpleadoDto>("empleados",
            DatosCatalogos.Empleado(ClienteApi.Unico("v"), vencimientoLicencia: hoy.AddDays(-1).ToString("yyyy-MM-dd")));
        await admin.Cliente.CrearAsync<EmpleadoDto>("empleados",
            DatosCatalogos.Empleado(ClienteApi.Unico("p"), vencimientoLicencia: hoy.AddDays(10).ToString("yyyy-MM-dd")));
        await admin.Cliente.CrearAsync<EmpleadoDto>("empleados", DatosCatalogos.Empleado(ClienteApi.Unico("m"), tipo: "MONITORA"));

        var todos = await (await admin.Cliente.GetAsync("/api/v1/conductores")).LeerAsync<ListaPaginada<ConductorDto>>();
        Assert.Equal(2, todos.Total);
        Assert.All(todos.Items, c => Assert.Null(c.Asignacion));
        Assert.Equal("JC", todos.Items[0].Iniciales);
        Assert.StartsWith("CC ", todos.Items[0].Documento);

        var vencidas = await (await admin.Cliente.GetAsync("/api/v1/conductores?licencia=VENCIDA&asignacion=SIN")).LeerAsync<ListaPaginada<ConductorDto>>();
        Assert.Equal("VENCIDA", Assert.Single(vencidas.Items).EstadoLicencia);
        Assert.Empty((await (await admin.Cliente.GetAsync("/api/v1/conductores?asignacion=CON")).LeerAsync<ListaPaginada<ConductorDto>>()).Items);
    }
}
