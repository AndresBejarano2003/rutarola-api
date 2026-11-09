using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Autenticacion;
using RutaRola.Application.Operacion;
using RutaRola.Application.Personas;
using RutaRola.Domain.Enums;
using RutaRola.Tests.Infraestructura;

namespace RutaRola.Tests.Perfil;

/// <summary>Mi cuenta (datos y foto propios) y fotos de estudiantes, acudientes y empleados.</summary>
[Collection(ColeccionBaseDatos.Nombre)]
public class PerfilTests(BaseDatosPrueba bd)
{
    /// <summary>PNG de 1×1.</summary>
    public const string Foto = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    [Fact]
    public async Task El_administrador_cambia_su_nombre_telefono_correo_y_foto_y_la_sesion_la_trae()
    {
        var admin = await bd.AdminNuevoAsync();
        var perfil = await (await admin.Cliente.GetAsync("/api/v1/auth/perfil")).LeerAsync<PerfilDto>();
        Assert.True(perfil.EditaNombre);
        Assert.Equal("", perfil.Foto);

        var correo = $"{ClienteApi.Unico("admin")}@correo.com";
        var sesion = await (await admin.Cliente.PutAsJsonAsync("/api/v1/auth/perfil",
            new { nombre = "Ana Gómez", correo, telefono = "3001234567", foto = Foto }, ClienteApi.Json)).LeerAsync<SesionDto>();
        Assert.Equal(("Ana Gómez", correo, Foto), (sesion.Nombre, sesion.Correo, sesion.Imagen));
        Assert.Equal(Foto, (await (await admin.Cliente.GetAsync("/api/v1/auth/me")).LeerAsync<SesionDto>()).Imagen);
        Assert.Equal(Foto, (await bd.LoginAsync(admin.Empresa.UsuarioAdmin, admin.Empresa.ContrasenaAdmin)).Sesion.Imagen);

        // "" quita la foto; un archivo que no es imagen se rechaza; el nombre es obligatorio.
        Assert.Equal("", (await (await admin.Cliente.PutAsJsonAsync("/api/v1/auth/perfil",
            new { nombre = "Ana Gómez", correo, telefono = "", foto = "" }, ClienteApi.Json)).LeerAsync<SesionDto>()).Imagen);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Cliente.PutAsJsonAsync("/api/v1/auth/perfil",
            new { nombre = "Ana", correo, telefono = "", foto = "data:application/pdf;base64,AAAA" }, ClienteApi.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.Cliente.PutAsJsonAsync("/api/v1/auth/perfil",
            new { nombre = "", correo, telefono = "", foto = "" }, ClienteApi.Json)).StatusCode);

        // El correo de otro usuario no se puede tomar.
        var otro = await bd.AdminNuevoAsync();
        var duplicado = await otro.Cliente.PutAsJsonAsync("/api/v1/auth/perfil", new { nombre = "Otro", correo, telefono = "", foto = "" }, ClienteApi.Json);
        Assert.Contains("ya está en uso", await duplicado.MensajeErrorAsync(HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task El_conductor_actualiza_su_telefono_y_foto_en_su_registro_de_empleado_sin_cambiar_su_nombre()
    {
        var e = await bd.EscenarioAsync(1);
        var perfil = await (await e.ClienteConductor.GetAsync("/api/v1/auth/perfil")).LeerAsync<PerfilDto>();
        Assert.False(perfil.EditaNombre);

        var sesion = await (await e.ClienteConductor.PutAsJsonAsync("/api/v1/auth/perfil",
            new { nombre = "Otro nombre", correo = "", telefono = "3109998877", foto = Foto }, ClienteApi.Json)).LeerAsync<SesionDto>();
        Assert.Equal(Foto, sesion.Imagen);
        Assert.Equal(perfil.Nombre, sesion.Nombre);

        var empleado = await (await e.Admin.Cliente.GetAsync($"/api/v1/empleados/{e.Conductor.Id}")).LeerAsync<EmpleadoDto>();
        Assert.Equal(("3109998877", Foto), (empleado.Telefono, empleado.Foto));
        var conductores = await (await e.Admin.Cliente.GetAsync("/api/v1/conductores")).LeerAsync<ListaPrueba<ConductorDto>>();
        Assert.Equal(Foto, conductores.Items.Single(c => c.Id == e.Conductor.Id).Foto);

        // La monitora ve la foto de su compañero en sus rutas del día.
        var rutas = await (await e.ClienteMonitora!.GetAsync("/api/v1/recorridos/del-dia")).LeerAsync<List<RutaAsignadaDto>>();
        Assert.Equal(Foto, rutas.Single().Companero!.Foto);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await e.ClienteConductor.PutAsJsonAsync("/api/v1/auth/perfil",
            new { nombre = "", correo = "", telefono = "", foto = "" }, ClienteApi.Json)).StatusCode);
    }

    [Fact]
    public async Task El_estudiante_guarda_su_foto_y_la_auditoria_no_copia_la_imagen()
    {
        var admin = await bd.AdminNuevoAsync();
        var (colegio, acudiente, _) = await admin.Cliente.CrearEstudianteAsync();
        var datos = DatosCatalogos.Estudiante(acudiente.Id, colegio.Id);
        datos["foto"] = Foto;
        var estudiante = await admin.Cliente.CrearAsync<EstudianteDto>("estudiantes", datos);
        Assert.Equal(Foto, estudiante.Foto);
        var lista = await (await admin.Cliente.GetAsync("/api/v1/estudiantes?tamano=50")).LeerAsync<ListaPrueba<EstudianteDto>>();
        Assert.Equal(Foto, lista.Items.Single(x => x.Id == estudiante.Id).Foto);

        datos["foto"] = "";
        Assert.Equal("", (await (await admin.Cliente.PutAsJsonAsync($"/api/v1/estudiantes/{estudiante.Id}", datos, ClienteApi.Json)).LeerAsync<EstudianteDto>()).Foto);

        await using var db = bd.Contexto(null, modoSistema: true);
        var registros = await db.Auditoria.Where(a => a.IdRegistro == estudiante.Id && a.TablaAfectada == "estudiantes").OrderBy(a => a.Id).ToListAsync();
        Assert.Equal([AccionAuditoria.Crear, AccionAuditoria.Actualizar], registros.Select(a => a.Accion));
        Assert.All(registros, a => Assert.DoesNotContain("base64", a.ValorNuevo ?? ""));
        Assert.Contains("(cambiada)", registros[0].ValorNuevo);
    }

    [Fact]
    public async Task La_monitora_ve_las_fotos_de_los_estudiantes_de_su_recorrido_y_otros_no()
    {
        var e = await bd.EscenarioAsync(2);
        var datos = DatosCatalogos.Estudiante(e.Acudiente.Id, e.Colegio.Id, documento: e.Estudiantes[0].NumeroDocumento, primerNombre: e.Estudiantes[0].PrimerNombre);
        datos["grado"] = e.Estudiantes[0].Grado;
        datos["foto"] = Foto;
        (await e.Admin.Cliente.PutAsJsonAsync($"/api/v1/estudiantes/{e.Estudiantes[0].Id}", datos, ClienteApi.Json)).EnsureSuccessStatusCode();
        var r = await (await e.ClienteConductor.IniciarAsync(e.Asignacion.Id)).LeerAsync<RecorridoDto>(HttpStatusCode.Created);

        var fotos = await (await e.ClienteMonitora!.GetAsync($"/api/v1/recorridos/{r.Id}/fotos")).LeerAsync<List<FotoEstudianteDto>>();
        Assert.Equal([new FotoEstudianteDto(e.Estudiantes[0].Id, Foto)], fotos);
        Assert.Single(await (await e.Admin.Cliente.GetAsync($"/api/v1/recorridos/{r.Id}/fotos")).LeerAsync<List<FotoEstudianteDto>>());

        // Otro conductor de la misma empresa, un acudiente o un admin de otra empresa no las ven.
        var acudiente = await bd.ClienteDeAsync(e.Acudiente.Usuario, "Clave-123");
        Assert.Equal(HttpStatusCode.Forbidden, (await acudiente.GetAsync($"/api/v1/recorridos/{r.Id}/fotos")).StatusCode);
        var otra = await bd.AdminNuevoAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await otra.Cliente.GetAsync($"/api/v1/recorridos/{r.Id}/fotos")).StatusCode);
    }

    public record ListaPrueba<T>(List<T> Items, int Total);
}
