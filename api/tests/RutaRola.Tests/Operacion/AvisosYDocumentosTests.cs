using System.Net;
using System.Net.Http.Json;
using RutaRola.Application.Autenticacion;
using RutaRola.Application.Comunicacion;
using RutaRola.Application.Configuracion;
using RutaRola.Application.Empresas;
using RutaRola.Application.Operacion;
using RutaRola.Domain.Enums;
using RutaRola.Tests.Infraestructura;

namespace RutaRola.Tests.Operacion;

/// <summary>Avisos de ausencia del acudiente, historial de documentos y foto del vehículo, formato de los PDF y tema.</summary>
[Collection(ColeccionBaseDatos.Nombre)]
public class AvisosYDocumentosTests(BaseDatosPrueba bd)
{
    private const string Pdf = "data:application/pdf;base64,JVBERi0xLjQKJcfsj6IKMSAwIG9iago8PD4+CmVuZG9iagp0cmFpbGVyCjw8Pj4KJSVFT0YK";
    private const string Foto = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    [Fact]
    public async Task El_acudiente_avisa_que_no_va_y_la_ruta_lo_confirma()
    {
        var e = await bd.EscenarioAsync();
        var juan = e.Estudiantes[0];
        var acudiente = await bd.ClienteDeAsync(e.Acudiente.Usuario, "Clave-123");
        var hoy = DatosOperacion.Hoy.ToString("yyyy-MM-dd");
        object Aviso(Guid estudiante, string fecha, string motivo = "NO_ASISTE", string? trayecto = null) =>
            new { estudianteId = estudiante, fecha, motivo, trayecto, nota = "Tiene fiebre" };

        // Validaciones: fecha pasada, trayecto con NO_ASISTE, estudiante ajeno.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await acudiente.PostAsJsonAsync("/api/v1/portal/avisos",
            Aviso(juan.Id, DatosOperacion.Hoy.AddDays(-1).ToString("yyyy-MM-dd")), ClienteApi.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await acudiente.PostAsJsonAsync("/api/v1/portal/avisos",
            Aviso(juan.Id, hoy, trayecto: "RECOGIDA"), ClienteApi.Json)).StatusCode);
        var otra = await bd.EscenarioAsync(estudiantes: 1, conMonitora: false);
        Assert.Equal(HttpStatusCode.NotFound, (await acudiente.PostAsJsonAsync("/api/v1/portal/avisos",
            Aviso(otra.Estudiantes[0].Id, hoy), ClienteApi.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await e.ClienteConductor.PostAsJsonAsync("/api/v1/portal/avisos",
            Aviso(juan.Id, hoy), ClienteApi.Json)).StatusCode);

        var aviso = await (await acudiente.PostAsJsonAsync("/api/v1/portal/avisos", Aviso(juan.Id, hoy), ClienteApi.Json))
            .LeerAsync<AvisoAusenciaDto>(HttpStatusCode.Created);
        Assert.Equal(("", MotivoAusencia.NoAsiste, false), (aviso.Trayecto, aviso.Motivo, aviso.Confirmado));
        Assert.Equal(HttpStatusCode.Conflict, (await acudiente.PostAsJsonAsync("/api/v1/portal/avisos",
            Aviso(juan.Id, hoy, "POR_SU_CUENTA", "ENTREGA"), ClienteApi.Json)).StatusCode);
        var portal = await (await acudiente.GetAsync("/api/v1/portal")).LeerAsync<PortalAcudienteDto>();
        Assert.Equal(aviso.Id, Assert.Single(Assert.Single(portal.Estudiantes, x => x.Id == juan.Id).Avisos).Id);

        // La monitora lo ve en sus rutas y en la parada del recorrido.
        var rutas = await (await e.ClienteMonitora!.GetAsync("/api/v1/recorridos/del-dia")).LeerAsync<List<RutaAsignadaDto>>();
        Assert.Equal(1, Assert.Single(rutas).AvisosAusencia);
        var recorrido = await (await e.ClienteMonitora.IniciarAsync(e.Asignacion.Id)).LeerAsync<RecorridoDto>(HttpStatusCode.Created);
        var parada = Assert.Single(recorrido.Paradas, p => p.EstudianteId == juan.Id);
        Assert.Equal(("Tiene fiebre", false), (parada.Aviso?.Nota, parada.Aviso?.Confirmado));
        Assert.All(recorrido.Paradas.Where(p => p.EstudianteId != juan.Id), p => Assert.Null(p.Aviso));

        // Confirma: queda AUSENTE y el aviso confirmado (el acudiente ya no lo cancela).
        var marcado = await (await e.ClienteMonitora.PatchAsJsonAsync($"/api/v1/recorridos/{recorrido.Id}/paradas",
            new { estudianteIds = new[] { juan.Id }, estado = "AUSENTE", motivoAusencia = "NO_ASISTE" }, ClienteApi.Json)).LeerAsync<RecorridoDto>();
        Assert.True(Assert.Single(marcado.Paradas, p => p.EstudianteId == juan.Id).Aviso?.Confirmado);
        var delDia = await (await e.Admin.Cliente.GetAsync($"/api/v1/avisos-ausencia?fecha={hoy}")).LeerAsync<List<AvisoAusenciaDto>>();
        var admin = Assert.Single(delDia);
        Assert.True(admin.Confirmado);
        Assert.StartsWith(e.Monitora!.PrimerNombre, admin.ConfirmadoPor);
        Assert.NotEqual("", admin.Ruta);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await acudiente.DeleteAsync($"/api/v1/portal/avisos/{aviso.Id}")).StatusCode);

        // Deshacer lo reabre y entonces sí se puede cancelar.
        await e.ClienteMonitora.MarcarAsync(recorrido.Id, "PENDIENTE", juan.Id);
        Assert.False(Assert.Single(await (await e.Admin.Cliente.GetAsync("/api/v1/avisos-ausencia")).LeerAsync<List<AvisoAusenciaDto>>()).Confirmado);
        Assert.Equal(HttpStatusCode.NoContent, (await acudiente.DeleteAsync($"/api/v1/portal/avisos/{aviso.Id}")).StatusCode);

        // Aislamiento: la otra empresa no ve los avisos.
        Assert.Empty(await (await otra.Admin.Cliente.GetAsync("/api/v1/avisos-ausencia")).LeerAsync<List<AvisoAusenciaDto>>());
    }

    [Fact]
    public async Task El_vehiculo_tiene_foto_e_historial_de_documentos_con_pdf()
    {
        var admin = await bd.AdminNuevoAsync();
        var c = admin.Cliente;
        var datos = (Dictionary<string, object?>)System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(
            System.Text.Json.JsonSerializer.Serialize(DatosCatalogos.Vehiculo(DatosOperacion.Placa(), "7")))!;
        datos["foto"] = "no-es-imagen";
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/v1/vehiculos", datos, ClienteApi.Json)).StatusCode);
        datos["foto"] = Foto;
        var vehiculo = await c.CrearAsync<VehiculoDto>("vehiculos", datos);
        Assert.Equal(Foto, vehiculo.Foto);
        var url = $"/api/v1/vehiculos/{vehiculo.Id}/documentos";

        // Cada vencimiento del formulario queda en el historial.
        var inicial = await (await c.GetAsync(url)).LeerAsync<List<DocumentoHistorialDto>>();
        Assert.Equal(4, inicial.Count);
        Assert.All(inicial, d => Assert.True(d.Vigente));

        // Un SOAT renovado con su PDF pasa a ser el vigente y actualiza el vehículo.
        var nuevo = await (await c.PostAsJsonAsync(url, new
        {
            tipo = "SOAT", numero = "SOAT-998877", entidad = "Seguros del Estado", fechaExpedicion = "2030-01-31", fechaVencimiento = "2031-01-31",
            observaciones = "", archivo = new { nombre = "soat.pdf", tipo = "application/pdf", tamano = 70, url = Pdf },
        }, ClienteApi.Json)).LeerAsync<DocumentoHistorialDto>(HttpStatusCode.Created);
        Assert.True(nuevo.Vigente);
        Assert.StartsWith("data:application/pdf;base64,", nuevo.Archivo?.Url);
        Assert.Equal(new DateOnly(2031, 1, 31), (await (await c.GetAsync($"/api/v1/vehiculos/{vehiculo.Id}")).LeerAsync<VehiculoDto>()).VencimientoSoat);
        var lista = await (await c.GetAsync(url)).LeerAsync<List<DocumentoHistorialDto>>();
        Assert.Equal(5, lista.Count);
        Assert.Equal("", lista.Single(d => d.Id == nuevo.Id).Archivo?.Url); // el listado no trae el contenido
        Assert.False(lista.Single(d => d.Tipo == TipoDocumentoVehiculo.Soat && d.Id != nuevo.Id).Vigente);

        // Vencimiento anterior a la expedición → 400; OTRO no cambia el vehículo.
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync(url, new { tipo = "OTRO", fechaExpedicion = "2030-05-01", fechaVencimiento = "2030-01-01" }, ClienteApi.Json)).StatusCode);

        // Eliminarlo devuelve el vencimiento anterior.
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"{url}/{nuevo.Id}")).StatusCode);
        Assert.Equal(new DateOnly(2030, 1, 31), (await (await c.GetAsync($"/api/v1/vehiculos/{vehiculo.Id}")).LeerAsync<VehiculoDto>()).VencimientoSoat);

        // Otra empresa no ve ni modifica el historial.
        var otra = await bd.AdminNuevoAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await otra.Cliente.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otra.Cliente.PostAsJsonAsync(url, new { tipo = "SOAT", fechaVencimiento = "2031-01-01" }, ClienteApi.Json)).StatusCode);
    }

    [Fact]
    public async Task La_empresa_parametriza_sus_pdf_y_el_tema()
    {
        var e = await bd.EscenarioAsync(estudiantes: 1, conMonitora: false);
        var c = e.Admin.Cliente;
        var formato = await (await c.GetAsync("/api/v1/configuracion/documentos")).LeerAsync<FormatoDocumentosDto>();
        Assert.False(formato.Personalizado);
        Assert.Equal(e.Admin.Empresa.Nit, formato.Nit);

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/v1/configuracion/documentos",
            formato with { ColorPrincipal = "rojo" }, ClienteApi.Json)).StatusCode);
        var guardado = await (await c.PutAsJsonAsync("/api/v1/configuracion/documentos",
            formato with { Logo = Foto, Eslogan = "Llegamos a tiempo", ColorPrincipal = "#facc15", NotaFactura = "Consignar a Bancolombia" }, ClienteApi.Json))
            .LeerAsync<FormatoDocumentosDto>();
        Assert.Equal((true, "#FACC15", "Llegamos a tiempo"), (guardado.Personalizado, guardado.ColorPrincipal, guardado.Eslogan));
        // El conductor lo lee (no lo cambia); otra empresa ve el suyo.
        Assert.Equal("Llegamos a tiempo", (await (await e.ClienteConductor.GetAsync("/api/v1/configuracion/documentos")).LeerAsync<FormatoDocumentosDto>()).Eslogan);
        Assert.Equal(HttpStatusCode.Forbidden, (await e.ClienteConductor.PutAsJsonAsync("/api/v1/configuracion/documentos", guardado, ClienteApi.Json)).StatusCode);
        var otra = await bd.AdminNuevoAsync();
        Assert.False((await (await otra.Cliente.GetAsync("/api/v1/configuracion/documentos")).LeerAsync<FormatoDocumentosDto>()).Personalizado);

        // Tema: lo elige el administrador y llega en la sesión de todos los usuarios de la empresa.
        Assert.Equal(TemaApariencia.Claro, (await (await c.PutAsJsonAsync("/api/v1/empresas/actual/tema", new { tema = "CLARO" }, ClienteApi.Json)).LeerAsync<TemaDto>()).Tema);
        var login = await bd.LoginAsync(e.Admin.Empresa.UsuarioAdmin, e.Admin.Empresa.ContrasenaAdmin);
        Assert.Equal(TemaApariencia.Claro, login.Sesion.Empresa!.Tema);
        Assert.Equal(TemaApariencia.Claro, (await (await e.ClienteConductor.GetAsync("/api/v1/empresas/actual/tema")).LeerAsync<TemaDto>()).Tema);
        Assert.Equal(HttpStatusCode.Forbidden, (await e.ClienteConductor.PutAsJsonAsync("/api/v1/empresas/actual/tema", new { tema = "OSCURO" }, ClienteApi.Json)).StatusCode);
        Assert.Equal(TemaApariencia.Oscuro, (await (await otra.Cliente.GetAsync("/api/v1/empresas/actual/tema")).LeerAsync<TemaDto>()).Tema);
    }
}
