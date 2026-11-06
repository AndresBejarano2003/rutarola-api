using System.Net;
using System.Net.Http.Json;
using RutaRola.Application.Comun;
using RutaRola.Application.Personas;
using RutaRola.Domain.Enums;
using RutaRola.Tests.Infraestructura;

namespace RutaRola.Tests.Catalogos;

/// <summary>Empleados, acudientes y estudiantes: reglas, credenciales de acceso y adjuntos.</summary>
[Collection(ColeccionBaseDatos.Nombre)]
public class PersonasTests(BaseDatosPrueba bd)
{
    [Fact]
    public async Task El_empleado_creado_ingresa_con_su_usuario_y_el_rol_de_su_tipo()
    {
        var admin = await bd.AdminNuevoAsync();
        var usuario = ClienteApi.Unico("Conductor");

        var empleado = await admin.Cliente.CrearAsync<EmpleadoDto>("empleados", DatosCatalogos.Empleado(usuario));

        Assert.Equal(usuario.ToLowerInvariant(), empleado.Usuario);
        Assert.Equal("", empleado.Contrasena);
        Assert.Equal(TipoEmpleado.Conductor, empleado.TipoEmpleado);
        Assert.Equal("", empleado.SegundoApellido);
        var login = await bd.LoginAsync(usuario, "Clave-123");
        Assert.Equal("CONDUCTOR", login.Sesion.Rol);
        Assert.Equal(empleado.Id, login.Sesion.EmpleadoId);
    }

    [Fact]
    public async Task El_conductor_necesita_licencia_y_la_monitora_no()
    {
        var admin = await bd.AdminNuevoAsync();
        var sinLicencia = DatosCatalogos.Empleado(ClienteApi.Unico("cond"));
        sinLicencia["numeroLicencia"] = "";

        var respuesta = await admin.Cliente.PostAsJsonAsync("/api/v1/empleados", sinLicencia, ClienteApi.Json);
        Assert.Equal("Ingresa el número de licencia del conductor.", await respuesta.MensajeErrorAsync(HttpStatusCode.BadRequest));

        var monitora = await admin.Cliente.CrearAsync<EmpleadoDto>("empleados", DatosCatalogos.Empleado(ClienteApi.Unico("moni"), tipo: "MONITORA"));
        Assert.Equal("", monitora.NumeroLicencia);
        Assert.Null(monitora.VencimientoLicencia);
    }

    [Fact]
    public async Task La_contrasena_es_obligatoria_al_crear()
    {
        var admin = await bd.AdminNuevoAsync();

        var respuesta = await admin.Cliente.PostAsJsonAsync("/api/v1/acudientes", DatosCatalogos.Acudiente(ClienteApi.Unico("acu"), contrasena: null), ClienteApi.Json);

        Assert.Equal("La contraseña debe tener al menos 6 caracteres.", await respuesta.MensajeErrorAsync(HttpStatusCode.UnprocessableEntity));
    }

    [Fact]
    public async Task El_usuario_es_unico_entre_empleados_acudientes_y_empresas()
    {
        var admin = await bd.AdminNuevoAsync();
        var otra = await bd.AdminNuevoAsync();
        var usuario = ClienteApi.Unico("compartido");
        await admin.Cliente.CrearAsync<EmpleadoDto>("empleados", DatosCatalogos.Empleado(usuario));

        var acudiente = await admin.Cliente.PostAsJsonAsync("/api/v1/acudientes", DatosCatalogos.Acudiente(usuario.ToUpperInvariant()), ClienteApi.Json);
        Assert.Equal($"El usuario \"{usuario.ToUpperInvariant()}\" ya está en uso. Elige otro.", await acudiente.MensajeErrorAsync(HttpStatusCode.Conflict));

        var enOtraEmpresa = await otra.Cliente.PostAsJsonAsync("/api/v1/empleados", DatosCatalogos.Empleado(usuario), ClienteApi.Json);
        Assert.Equal($"El usuario \"{usuario}\" ya está en uso. Elige otro.", await enOtraEmpresa.MensajeErrorAsync(HttpStatusCode.Conflict));

        var admin2 = await admin.Cliente.PostAsJsonAsync("/api/v1/acudientes", DatosCatalogos.Acudiente(otra.Empresa.UsuarioAdmin), ClienteApi.Json);
        Assert.Equal(HttpStatusCode.Conflict, admin2.StatusCode);
    }

    [Fact]
    public async Task Al_editar_sin_contrasena_se_conserva_y_al_cambiarla_se_cierran_sus_sesiones()
    {
        var admin = await bd.AdminNuevoAsync();
        var usuario = ClienteApi.Unico("acu");
        var acudiente = await admin.Cliente.CrearAsync<AcudienteDto>("acudientes", DatosCatalogos.Acudiente(usuario));
        var sesion = await bd.LoginAsync(usuario, "Clave-123");

        var sinClave = DatosCatalogos.Acudiente(usuario, acudiente.NumeroDocumento, primerNombre: "Marta", contrasena: null);
        var editado = await (await admin.Cliente.PutAsJsonAsync($"/api/v1/acudientes/{acudiente.Id}", sinClave, ClienteApi.Json)).LeerAsync<AcudienteDto>();
        Assert.Equal("Marta", editado.PrimerNombre);
        await bd.LoginAsync(usuario, "Clave-123");

        var nuevaClave = DatosCatalogos.Acudiente(usuario, acudiente.NumeroDocumento, contrasena: "Nueva-456");
        await (await admin.Cliente.PutAsJsonAsync($"/api/v1/acudientes/{acudiente.Id}", nuevaClave, ClienteApi.Json)).LeerAsync<AcudienteDto>();
        Assert.Equal(HttpStatusCode.Unauthorized, (await bd.PostLoginAsync(usuario, "Clave-123")).StatusCode);
        Assert.Equal("ACUDIENTE", (await bd.LoginAsync(usuario, "Nueva-456")).Sesion.Rol);

        var refresh = await bd.Api.CreateClient().PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = sesion.Tokens.RefreshToken }, ClienteApi.Json);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Cambiar_el_tipo_de_empleado_cambia_su_rol()
    {
        var admin = await bd.AdminNuevoAsync();
        var usuario = ClienteApi.Unico("emp");
        var empleado = await admin.Cliente.CrearAsync<EmpleadoDto>("empleados", DatosCatalogos.Empleado(usuario));

        var monitora = DatosCatalogos.Empleado(usuario, tipo: "MONITORA", contrasena: null, documento: empleado.NumeroDocumento);
        await (await admin.Cliente.PutAsJsonAsync($"/api/v1/empleados/{empleado.Id}", monitora, ClienteApi.Json)).LeerAsync<EmpleadoDto>();

        Assert.Equal("MONITORA", (await bd.LoginAsync(usuario, "Clave-123")).Sesion.Rol);
    }

    [Fact]
    public async Task El_contrato_se_guarda_y_solo_el_detalle_trae_el_contenido()
    {
        var admin = await bd.AdminNuevoAsync();
        var contenido = Convert.ToBase64String("%PDF-1.4 contrato"u8.ToArray());
        var solicitud = DatosCatalogos.Empleado(ClienteApi.Unico("emp"));
        solicitud["contrato"] = new { nombre = "contrato.pdf", tipo = "application/pdf", tamano = 17, url = $"data:application/pdf;base64,{contenido}" };

        var empleado = await admin.Cliente.CrearAsync<EmpleadoDto>("empleados", solicitud);
        Assert.Equal($"data:application/pdf;base64,{contenido}", empleado.Contrato!.Url);
        Assert.Equal(17, empleado.Contrato.Tamano);

        var lista = await (await admin.Cliente.GetAsync("/api/v1/empleados")).LeerAsync<ListaPaginada<EmpleadoDto>>();
        Assert.Equal("", Assert.Single(lista.Items).Contrato!.Url);

        // Sin cambios en el adjunto (url de la lista) se conserva; null lo quita.
        solicitud["contrasena"] = "";
        solicitud["contrato"] = new { nombre = "contrato.pdf", tipo = "application/pdf", tamano = 17, url = "" };
        var igual = await (await admin.Cliente.PutAsJsonAsync($"/api/v1/empleados/{empleado.Id}", solicitud, ClienteApi.Json)).LeerAsync<EmpleadoDto>();
        Assert.Equal(empleado.Contrato.Url, igual.Contrato!.Url);

        solicitud["contrato"] = null;
        var sinContrato = await (await admin.Cliente.PutAsJsonAsync($"/api/v1/empleados/{empleado.Id}", solicitud, ClienteApi.Json)).LeerAsync<EmpleadoDto>();
        Assert.Null(sinContrato.Contrato);
        await using var db = bd.Contexto(admin.Empresa.Id);
        Assert.Equal(0, db.Archivos.Count());
    }

    [Fact]
    public async Task El_contrato_debe_ser_pdf_o_imagen()
    {
        var admin = await bd.AdminNuevoAsync();
        var solicitud = DatosCatalogos.Empleado(ClienteApi.Unico("emp"));
        solicitud["contrato"] = new { nombre = "virus.exe", tipo = "application/x-msdownload", tamano = 3, url = "data:application/x-msdownload;base64,AAAA" };

        var respuesta = await admin.Cliente.PostAsJsonAsync("/api/v1/empleados", solicitud, ClienteApi.Json);

        Assert.Equal("El contrato debe ser un PDF o una imagen.", await respuesta.MensajeErrorAsync(HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task Eliminar_al_empleado_libera_su_usuario()
    {
        var admin = await bd.AdminNuevoAsync();
        var usuario = ClienteApi.Unico("emp");
        var empleado = await admin.Cliente.CrearAsync<EmpleadoDto>("empleados", DatosCatalogos.Empleado(usuario));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.Cliente.DeleteAsync($"/api/v1/empleados/{empleado.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await bd.PostLoginAsync(usuario, "Clave-123")).StatusCode);
        await admin.Cliente.CrearAsync<AcudienteDto>("acudientes", DatosCatalogos.Acudiente(usuario));
    }

    [Fact]
    public async Task El_documento_es_unico_por_tipo_de_persona()
    {
        var admin = await bd.AdminNuevoAsync();
        var documento = DatosCatalogos.Documento();
        await admin.Cliente.CrearAsync<AcudienteDto>("acudientes", DatosCatalogos.Acudiente(ClienteApi.Unico("a"), documento));

        var repetido = await admin.Cliente.PostAsJsonAsync("/api/v1/acudientes", DatosCatalogos.Acudiente(ClienteApi.Unico("b"), documento), ClienteApi.Json);
        Assert.Equal($"Ya existe un acudiente con el documento CC {documento}.", await repetido.MensajeErrorAsync(HttpStatusCode.Conflict));

        // Un empleado sí puede tener el mismo documento que un acudiente (son tablas distintas).
        await admin.Cliente.CrearAsync<EmpleadoDto>("empleados", DatosCatalogos.Empleado(ClienteApi.Unico("c"), documento: documento));
    }

    [Fact]
    public async Task Busca_personas_por_nombre_completo_sin_tildes_y_ordena_por_apellido()
    {
        var admin = await bd.AdminNuevoAsync();
        var maria = DatosCatalogos.Acudiente(ClienteApi.Unico("a"));
        var carlos = DatosCatalogos.Acudiente(ClienteApi.Unico("b"), primerNombre: "Carlos");
        carlos["primerApellido"] = "Álvarez";
        await admin.Cliente.CrearAsync<AcudienteDto>("acudientes", maria);
        await admin.Cliente.CrearAsync<AcudienteDto>("acudientes", carlos);

        var busqueda = await (await admin.Cliente.GetAsync("/api/v1/acudientes?texto=maria%20perez")).LeerAsync<ListaPaginada<AcudienteDto>>();
        Assert.Equal("María", Assert.Single(busqueda.Items).PrimerNombre);

        var opciones = await (await admin.Cliente.GetAsync("/api/v1/acudientes/opciones")).LeerAsync<List<Opcion>>();
        Assert.Equal(["Carlos Álvarez López", "María Pérez López"], opciones.Select(o => o.Nombre));
    }

    [Fact]
    public async Task El_estudiante_queda_con_su_acudiente_principal_y_se_filtra_por_el()
    {
        var admin = await bd.AdminNuevoAsync();
        var (colegio, acudiente, estudiante) = await admin.Cliente.CrearEstudianteAsync();
        Assert.Equal(acudiente.Id, estudiante.AcudienteId);
        Assert.Equal(colegio.Id, estudiante.ColegioId);

        var otro = await admin.Cliente.CrearAcudienteAsync();
        var cambio = DatosCatalogos.Estudiante(otro.Id, colegio.Id, documento: estudiante.NumeroDocumento);
        var editado = await (await admin.Cliente.PutAsJsonAsync($"/api/v1/estudiantes/{estudiante.Id}", cambio, ClienteApi.Json)).LeerAsync<EstudianteDto>();
        Assert.Equal(otro.Id, editado.AcudienteId);

        Assert.Empty((await (await admin.Cliente.GetAsync($"/api/v1/estudiantes?acudienteId={acudiente.Id}")).LeerAsync<ListaPaginada<EstudianteDto>>()).Items);
        var deOtro = await (await admin.Cliente.GetAsync($"/api/v1/estudiantes/opciones?acudienteId={otro.Id}")).LeerAsync<List<Opcion>>();
        Assert.Equal(estudiante.Id, Assert.Single(deOtro).Id);

        // El acudiente anterior ya se puede eliminar; el nuevo no (tiene un estudiante a cargo).
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Cliente.DeleteAsync($"/api/v1/acudientes/{acudiente.Id}")).StatusCode);
        var eliminar = await admin.Cliente.DeleteAsync($"/api/v1/acudientes/{otro.Id}");
        Assert.Equal("No se puede eliminar: tiene 1 estudiante(s) a cargo. Reasígnalos a otro acudiente o cámbialo a Inactivo.",
            await eliminar.MensajeErrorAsync(HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task El_grado_debe_ser_un_curso_activo_del_colegio()
    {
        var admin = await bd.AdminNuevoAsync();
        var colegio = await admin.Cliente.CrearColegioAsync();
        var acudiente = await admin.Cliente.CrearAcudienteAsync();

        var inactivo = await admin.Cliente.PostAsJsonAsync("/api/v1/estudiantes", DatosCatalogos.Estudiante(acudiente.Id, colegio.Id, grado: "7°"), ClienteApi.Json);
        Assert.Equal("El colegio Colegio San Cayetano no tiene activo el curso 7°.", await inactivo.MensajeErrorAsync(HttpStatusCode.UnprocessableEntity));

        var inexistente = await admin.Cliente.PostAsJsonAsync("/api/v1/estudiantes", DatosCatalogos.Estudiante(acudiente.Id, Guid.NewGuid()), ClienteApi.Json);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
    }
}
