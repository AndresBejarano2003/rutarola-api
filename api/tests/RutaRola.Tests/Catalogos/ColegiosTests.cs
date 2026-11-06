using System.Net;
using System.Net.Http.Json;
using RutaRola.Application.Comun;
using RutaRola.Application.Operacion;
using RutaRola.Tests.Infraestructura;

namespace RutaRola.Tests.Catalogos;

[Collection(ColeccionBaseDatos.Nombre)]
public class ColegiosTests(BaseDatosPrueba bd)
{
    [Fact]
    public async Task Crea_el_colegio_con_sus_cursos_ordenados_de_Jardin_a_Once()
    {
        var admin = await bd.AdminNuevoAsync();

        var colegio = await admin.Cliente.CrearColegioAsync();

        Assert.Equal(["Jardín", "1°", "7°"], colegio.Cursos.Select(c => c.Grado));
        Assert.Equal(new TimeOnly(7, 0), colegio.Cursos[0].HoraEntrada);
        Assert.Equal("INACTIVO", colegio.Cursos[2].Estado);
        Assert.Equal("ACTIVO", colegio.Estado);

        var json = await (await admin.Cliente.GetAsync($"/api/v1/colegios/{colegio.Id}")).Content.ReadAsStringAsync();
        Assert.Contains("\"horaEntrada\":\"07:00\"", json);
    }

    [Fact]
    public async Task El_NIT_es_unico_sin_importar_el_digito_de_verificacion()
    {
        var admin = await bd.AdminNuevoAsync();
        await admin.Cliente.CrearAsync<ColegioDto>("colegios", DatosCatalogos.Colegio(nit: "891180123-4"));

        var repetido = await admin.Cliente.PostAsJsonAsync("/api/v1/colegios", DatosCatalogos.Colegio(nit: "891180123-9"), ClienteApi.Json);

        Assert.Equal("Ya existe un colegio con el NIT 891180123-9.", await repetido.MensajeErrorAsync(HttpStatusCode.Conflict));

        // Otra empresa sí puede registrar el mismo colegio.
        var otra = await bd.AdminNuevoAsync();
        await otra.Cliente.CrearAsync<ColegioDto>("colegios", DatosCatalogos.Colegio(nit: "891180123-4"));
    }

    [Fact]
    public async Task La_hora_de_salida_debe_ser_posterior_a_la_de_entrada()
    {
        var admin = await bd.AdminNuevoAsync();
        var solicitud = DatosCatalogos.Colegio(cursos: [new { grado = "5°", horaEntrada = "13:00", horaSalida = "07:00", estado = "ACTIVO" }]);

        var respuesta = await admin.Cliente.PostAsJsonAsync("/api/v1/colegios", solicitud, ClienteApi.Json);

        Assert.Equal("En 5° la hora de salida debe ser posterior a la de entrada.", await respuesta.MensajeErrorAsync(HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task Editar_actualiza_agrega_y_quita_cursos_por_grado()
    {
        var admin = await bd.AdminNuevoAsync();
        var colegio = await admin.Cliente.CrearColegioAsync();

        var solicitud = DatosCatalogos.Colegio(nit: colegio.Nit, razonSocial: "Colegio Renovado", cursos:
        [
            new { grado = "Jardín", horaEntrada = "08:00", horaSalida = "12:00", estado = "ACTIVO" },
            new { grado = "11°", horaEntrada = "06:00", horaSalida = "14:00", estado = "ACTIVO" },
        ]);
        var editado = await (await admin.Cliente.PutAsJsonAsync($"/api/v1/colegios/{colegio.Id}", solicitud, ClienteApi.Json))
            .LeerAsync<ColegioDto>();

        Assert.Equal("Colegio Renovado", editado.RazonSocial);
        Assert.Equal(["Jardín", "11°"], editado.Cursos.Select(c => c.Grado));
        Assert.Equal(new TimeOnly(8, 0), editado.Cursos[0].HoraEntrada);
        Assert.NotNull(editado.FechaActualizacion);
    }

    [Fact]
    public async Task Lista_filtra_sin_tildes_pagina_y_ofrece_opciones_ordenadas()
    {
        var admin = await bd.AdminNuevoAsync();
        await admin.Cliente.CrearAsync<ColegioDto>("colegios", DatosCatalogos.Colegio(razonSocial: "Colegio San José"));
        await admin.Cliente.CrearAsync<ColegioDto>("colegios", DatosCatalogos.Colegio(razonSocial: "Colegio La Gaitana"));
        var inactivo = (Dictionary<string, object?>)Convertir(DatosCatalogos.Colegio(razonSocial: "Colegio Antiguo"));
        inactivo["estado"] = "INACTIVO";
        await admin.Cliente.CrearAsync<ColegioDto>("colegios", inactivo);

        var porTexto = await (await admin.Cliente.GetAsync("/api/v1/colegios?texto=JOSE&pagina=1&tamano=10")).LeerAsync<ListaPaginada<ColegioDto>>();
        Assert.Equal("Colegio San José", Assert.Single(porTexto.Items).RazonSocial);

        var activos = await (await admin.Cliente.GetAsync("/api/v1/colegios?estado=ACTIVO&pagina=1&tamano=1")).LeerAsync<ListaPaginada<ColegioDto>>();
        Assert.Equal(2, activos.Total);
        Assert.Equal("Colegio La Gaitana", Assert.Single(activos.Items).RazonSocial);

        var opciones = await (await admin.Cliente.GetAsync("/api/v1/colegios/opciones")).LeerAsync<List<Opcion>>();
        Assert.Equal(["Colegio Antiguo", "Colegio La Gaitana", "Colegio San José"], opciones.Select(o => o.Nombre));
    }

    [Fact]
    public async Task Pedir_todo_con_tamano_enorme_responde_con_el_maximo()
    {
        var admin = await bd.AdminNuevoAsync();

        var pagina = await (await admin.Cliente.GetAsync("/api/v1/colegios?pagina=1&tamano=9007199254740991")).LeerAsync<ListaPaginada<ColegioDto>>();

        Assert.Equal(Paginacion.TamanoMaximo, pagina.Tamano);
    }

    [Fact]
    public async Task No_se_elimina_si_tiene_estudiantes_y_se_explica_el_motivo()
    {
        var admin = await bd.AdminNuevoAsync();
        var (colegio, _, _) = await admin.Cliente.CrearEstudianteAsync();
        const string motivo = "No se puede eliminar: el colegio tiene 1 estudiante(s). Reasígnalos o cambia el colegio a Inactivo.";

        var consulta = await (await admin.Cliente.GetAsync($"/api/v1/colegios/{colegio.Id}/motivo-no-eliminar")).LeerAsync<RespuestaMotivo>();
        Assert.Equal(motivo, consulta.Motivo);

        var eliminar = await admin.Cliente.DeleteAsync($"/api/v1/colegios/{colegio.Id}");
        Assert.Equal(motivo, await eliminar.MensajeErrorAsync(HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task Sin_uso_se_elimina_con_sus_cursos()
    {
        var admin = await bd.AdminNuevoAsync();
        var colegio = await admin.Cliente.CrearColegioAsync();

        Assert.Null((await (await admin.Cliente.GetAsync($"/api/v1/colegios/{colegio.Id}/motivo-no-eliminar")).LeerAsync<RespuestaMotivo>()).Motivo);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Cliente.DeleteAsync($"/api/v1/colegios/{colegio.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Cliente.GetAsync($"/api/v1/colegios/{colegio.Id}")).StatusCode);
    }

    private static object Convertir(object anonimo) =>
        anonimo.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(anonimo));
}
