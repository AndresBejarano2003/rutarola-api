using System.Net;
using System.Net.Http.Json;
using RutaRola.Application.Operacion;
using RutaRola.Application.Personas;

namespace RutaRola.Tests.Infraestructura;

/// <summary>Solicitudes válidas de los catálogos (como las envía el frontend) para armar escenarios.</summary>
public static class DatosCatalogos
{
    public record Admin(ClienteApi.EmpresaPrueba Empresa, HttpClient Cliente);

    public static async Task<Admin> AdminNuevoAsync(this BaseDatosPrueba bd)
    {
        var empresa = await bd.CrearEmpresaAsync();
        return new Admin(empresa, await bd.ClienteDeAsync(empresa.UsuarioAdmin, empresa.ContrasenaAdmin));
    }

    private static int consecutivo = 1000;

    public static string Documento() => $"{Random.Shared.Next(100000, 999999)}{Interlocked.Increment(ref consecutivo)}";

    public static object Colegio(string? nit = null, string razonSocial = "Colegio San Cayetano", object[]? cursos = null) => new
    {
        nit = nit ?? ClienteApi.NitUnico(),
        razonSocial,
        direccion = "Carrera 5 # 12-40, Neiva",
        telefono = "6088712345",
        cursos = cursos ?? new object[]
        {
            new { grado = "1°", horaEntrada = "06:30", horaSalida = "13:00", estado = "ACTIVO" },
            new { grado = "Jardín", horaEntrada = "07:00", horaSalida = "12:00", estado = "ACTIVO" },
            new { grado = "7°", horaEntrada = "06:30", horaSalida = "13:00", estado = "INACTIVO" },
        },
        estado = "ACTIVO",
    };

    public static object Vehiculo(string placa, string numeroInterno, string vencimiento = "2030-01-31") => new
    {
        placa,
        numeroInterno,
        tipo = "BUSETA",
        marca = "Chevrolet",
        linea = "NPR",
        modelo = 2021,
        color = "Amarillo",
        capacidad = 19,
        vencimientoSoat = vencimiento,
        vencimientoTecnomecanica = "2030-02-28",
        vencimientoTarjetaOperacion = "2030-03-31",
        vencimientoPolizas = "2030-04-30",
        estado = "ACTIVO",
        observaciones = "",
    };

    public static Dictionary<string, object?> Empleado(string usuario, string tipo = "CONDUCTOR", string? contrasena = "Clave-123",
        string? documento = null, string vencimientoLicencia = "2030-06-30") => new()
    {
        ["tipoDocumento"] = "CC",
        ["numeroDocumento"] = documento ?? Documento(),
        ["primerNombre"] = "José",
        ["segundoNombre"] = "Luis",
        ["primerApellido"] = "Cárdenas",
        ["segundoApellido"] = "",
        ["tipoEmpleado"] = tipo,
        ["telefono"] = "3101112233",
        ["correo"] = "",
        ["direccion"] = "Carrera 5 # 12-40, Neiva",
        ["fechaIngreso"] = "2025-02-01",
        ["sueldo"] = 2200000,
        ["tipoContrato"] = "TERMINO_INDEFINIDO",
        ["contrato"] = null,
        ["numeroLicencia"] = tipo == "CONDUCTOR" ? "7698123" : "",
        ["categoriaLicencia"] = tipo == "CONDUCTOR" ? "C2" : "",
        ["vencimientoLicencia"] = tipo == "CONDUCTOR" ? vencimientoLicencia : "",
        ["usuario"] = usuario,
        ["contrasena"] = contrasena ?? "",
        ["estado"] = "ACTIVO",
    };

    public static Dictionary<string, object?> Acudiente(string usuario, string? documento = null, string primerNombre = "María",
        string? contrasena = "Clave-123") => new()
    {
        ["tipoDocumento"] = "CC",
        ["numeroDocumento"] = documento ?? Documento(),
        ["primerNombre"] = primerNombre,
        ["segundoNombre"] = "",
        ["primerApellido"] = "Pérez",
        ["segundoApellido"] = "López",
        ["telefono"] = "3001234567",
        ["telefonoAlterno"] = "",
        ["correo"] = "maria.perez@correo.com",
        ["direccion"] = "Calle 10 # 5-23, Neiva",
        ["usuario"] = usuario,
        ["contrasena"] = contrasena ?? "",
        ["estado"] = "ACTIVO",
    };

    public static Dictionary<string, object?> Estudiante(Guid acudienteId, Guid colegioId, string grado = "1°", string? documento = null,
        string primerNombre = "Juan") => new()
    {
        ["tipoDocumento"] = "TI",
        ["numeroDocumento"] = documento ?? Documento(),
        ["primerNombre"] = primerNombre,
        ["segundoNombre"] = "Sebastián",
        ["primerApellido"] = "Pérez",
        ["segundoApellido"] = "López",
        ["fechaNacimiento"] = "2013-04-12",
        ["direccion"] = "Calle 10 # 5-23, Neiva",
        ["acudienteId"] = acudienteId,
        ["colegioId"] = colegioId,
        ["grado"] = grado,
        ["jornada"] = "Mañana",
        ["estado"] = "ACTIVO",
        ["observaciones"] = "",
    };

    public static async Task<T> CrearAsync<T>(this HttpClient cliente, string recurso, object solicitud) =>
        await (await cliente.PostAsJsonAsync($"/api/v1/{recurso}", solicitud, ClienteApi.Json)).LeerAsync<T>(HttpStatusCode.Created);

    public static Task<ColegioDto> CrearColegioAsync(this HttpClient cliente) => cliente.CrearAsync<ColegioDto>("colegios", Colegio());

    public static Task<AcudienteDto> CrearAcudienteAsync(this HttpClient cliente) =>
        cliente.CrearAsync<AcudienteDto>("acudientes", Acudiente(ClienteApi.Unico("acu")));

    /// <summary>Colegio + acudiente + estudiante en 1°.</summary>
    public static async Task<(ColegioDto Colegio, AcudienteDto Acudiente, EstudianteDto Estudiante)> CrearEstudianteAsync(this HttpClient cliente)
    {
        var colegio = await cliente.CrearColegioAsync();
        var acudiente = await cliente.CrearAcudienteAsync();
        var estudiante = await cliente.CrearAsync<EstudianteDto>("estudiantes", Estudiante(acudiente.Id, colegio.Id));
        return (colegio, acudiente, estudiante);
    }
}
