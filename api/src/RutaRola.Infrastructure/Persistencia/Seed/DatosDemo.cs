using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RutaRola.Application.Comun;
using RutaRola.Domain.Comun;
using RutaRola.Domain.Enums;
using RutaRola.Domain.Operacion;
using RutaRola.Domain.Personas;
using RutaRola.Domain.Seguridad;

namespace RutaRola.Infrastructure.Persistencia.Seed;

/// <summary>
/// Datos de ejemplo de la empresa demo, iguales a los del modo local del frontend (colegios,
/// vehículos, empleados, acudientes y estudiantes de Neiva) para que las pantallas no arranquen
/// vacías al cambiar a HTTP. Solo fuera de producción, con <c>Seed:Demo:Habilitado</c> y la contraseña
/// de los usuarios demo en <c>Seed:Demo:Password</c>; se crean una sola vez (si la empresa no tiene colegios).
/// Usuarios: inicial del nombre + primer apellido (<c>jcardenas</c>, <c>mperez</c>…).
/// </summary>
internal class DatosDemo(RutaRolaDbContext db, IPasswordHasher<Usuario> hasher, TimeProvider reloj)
{
    public async Task<bool> SembrarAsync(Empresa empresa, IReadOnlyDictionary<string, Rol> roles, string contrasena, CancellationToken ct)
    {
        if (await db.Colegios.AnyAsync(c => c.IdEmpresa == empresa.Id, ct))
        {
            return false;
        }

        var hoy = Fechas.Hoy(reloj);
        var colegios = Colegios(empresa.Id);
        db.Colegios.AddRange(colegios.Values);
        var vehiculos = Vehiculos(empresa.Id, hoy).ToList();
        db.Vehiculos.AddRange(vehiculos);

        var empleados = Empleados(empresa.Id, hoy).ToList();
        foreach (var e in empleados)
        {
            e.IdUsuario = Usuario(empresa.Id, e, roles[e.TipoEmpleado == TipoEmpleado.Conductor ? Roles.Conductor : Roles.Monitora], contrasena);
            db.Empleados.Add(e);
        }

        var acudientes = Acudientes(empresa.Id);
        foreach (var a in acudientes.Values)
        {
            a.IdUsuario = Usuario(empresa.Id, a, roles[Roles.Acudiente], contrasena);
            db.Acudientes.Add(a);
        }

        var estudiantes = new List<Estudiante>();
        foreach (var (estudiante, acudiente, colegio) in Estudiantes(empresa.Id))
        {
            estudiante.IdColegio = colegios[colegio].Id;
            estudiante.Acudientes.Add(new EstudianteAcudiente { IdEstudiante = estudiante.Id, IdAcudiente = acudientes[acudiente].Id, EsPrincipal = true });
            db.Estudiantes.Add(estudiante);
            estudiantes.Add(estudiante);
        }

        // Rutas y asignaciones del frontend (rut-1..4, asg-1..3; R-04 sin asignar). Índices base 1 como est-N, veh-N, emp-N.
        Ruta Ruta(string codigo, string nombre, string colegio, string jornada, string sector, params int[] est) => new()
        {
            IdEmpresa = empresa.Id, Codigo = codigo, Nombre = nombre, IdColegio = colegios[colegio].Id, Jornada = jornada, Sector = sector,
            Estudiantes = est.Select((n, i) => new RutaEstudiante { IdEstudiante = estudiantes[n - 1].Id, OrdenRecogida = i + 1 }).ToList(),
        };
        var rutas = new[]
        {
            Ruta("R-01", "Norte", "col-5", "Mañana", "Cándido, Las Granjas, Santa Inés", 1, 2),
            Ruta("R-02", "Centro", "col-1", "Mañana", "Centro, Altico, Quebrada Seca", 3, 12),
            Ruta("R-03", "Sur", "col-2", "Tarde", "Timanco, Los Mártires, Limonar", 4, 5),
            Ruta("R-04", "Oriente", "col-3", "Única", "Ipanema, Calixto, Alberto Galindo", 6, 7),
        };
        db.Rutas.AddRange(rutas);
        AsignacionRuta Asignacion(int ruta, int vehiculo, int conductor, int monitora) => new()
        {
            IdEmpresa = empresa.Id, IdRuta = rutas[ruta - 1].Id, IdVehiculo = vehiculos[vehiculo - 1].Id,
            IdConductor = empleados[conductor - 1].Id, IdMonitora = empleados[monitora - 1].Id,
            FechaInicio = new DateOnly(hoy.Year, 2, 2), // Fecha fin vacía = indefinida: la demo sigue vigente.
        };
        db.AsignacionesRuta.AddRange(Asignacion(1, 1, 1, 6), Asignacion(2, 2, 2, 7), Asignacion(3, 3, 4, 8));
        return true;
    }

    private Guid Usuario(Guid idEmpresa, Persona p, Rol rol, string contrasena)
    {
        var usuario = new Usuario
        {
            IdEmpresa = idEmpresa,
            IdRol = rol.Id,
            Nombre = p.NombreCompleto,
            NombreUsuario = Busqueda.Normalizar($"{p.PrimerNombre[0]}{p.PrimerApellido}").Replace(" ", ""),
        };
        usuario.PasswordHash = hasher.HashPassword(usuario, contrasena);
        db.Usuarios.Add(usuario);
        return usuario.Id;
    }

    private static List<ColegioCurso> Cursos(string entrada, string salida, string desde = "Jardín", string hasta = "11°") =>
        Catalogos.Grados[Catalogos.OrdenGrado(desde)..(Catalogos.OrdenGrado(hasta) + 1)]
            .Select(g => new ColegioCurso { Grado = g, HoraEntrada = TimeOnly.Parse(entrada), HoraSalida = TimeOnly.Parse(salida) })
            .ToList();

    private static Dictionary<string, Colegio> Colegios(Guid idEmpresa)
    {
        Colegio Colegio(string nit, string razonSocial, string direccion, string telefono, List<ColegioCurso> cursos) =>
            new() { IdEmpresa = idEmpresa, Nit = nit, RazonSocial = razonSocial, Direccion = direccion, Telefono = telefono, Cursos = cursos };

        return new()
        {
            ["col-1"] = Colegio("891180123-4", "Colegio San Cayetano", "Carrera 5 # 12-40, Neiva", "6088712345", Cursos("06:30", "13:00")),
            ["col-2"] = Colegio("891180234-5", "Colegio Matre Paula", "Calle 21 # 8-15, Neiva", "6088723456",
                [.. Cursos("06:45", "12:45", hasta: "5°"), .. Cursos("12:30", "18:00", desde: "6°")]),
            ["col-3"] = Colegio("891180345-6", "Colegio La Gaitana", "Avenida La Toma # 3-20, Neiva", "6088734567", Cursos("07:00", "14:00")),
            ["col-4"] = Colegio("891180456-7", "Colegio Enrique Olaya Herrera", "Calle 7 # 30-11, Neiva", "6088745678",
                Cursos("06:30", "12:30", hasta: "5°")),
            ["col-5"] = Colegio("891180567-8", "Colegio San José", "Carrera 16 # 18-02, Neiva", "6088756789", Cursos("06:20", "13:20")),
        };
    }

    private static IEnumerable<Vehiculo> Vehiculos(Guid idEmpresa, DateOnly hoy)
    {
        Vehiculo Vehiculo(string placa, string interno, TipoVehiculo tipo, string marca, string linea, int anio, int capacidad,
            int[] dias, bool activo = true) => new()
        {
            IdEmpresa = idEmpresa, Placa = placa, NumeroInterno = interno, Tipo = tipo, Marca = marca, Linea = linea, Anio = anio,
            Color = "Amarillo", Capacidad = capacidad, Activo = activo,
            Documentos =
            [
                new() { TipoDocumento = TipoDocumentoVehiculo.Soat, FechaVencimiento = hoy.AddDays(dias[0]) },
                new() { TipoDocumento = TipoDocumentoVehiculo.Tecnomecanica, FechaVencimiento = hoy.AddDays(dias[1]) },
                new() { TipoDocumento = TipoDocumentoVehiculo.TarjetaOperacion, FechaVencimiento = hoy.AddDays(dias[2]) },
                new() { TipoDocumento = TipoDocumentoVehiculo.Polizas, FechaVencimiento = hoy.AddDays(dias[3]) },
            ],
        };

        yield return Vehiculo("WTN482", "101", TipoVehiculo.Buseta, "Chevrolet", "NPR", 2021, 19, [210, 150, 300, 210]);
        yield return Vehiculo("SXK915", "102", TipoVehiculo.Bus, "Hino", "FC9J", 2019, 32, [90, 18, 240, 90]);
        yield return Vehiculo("TMP307", "103", TipoVehiculo.Microbus, "Mercedes-Benz", "Sprinter", 2022, 15, [300, 280, 330, 300]);
        yield return Vehiculo("UFZ624", "104", TipoVehiculo.Buseta, "Volkswagen", "Delivery", 2018, 20, [-10, 60, 120, 120]);
        yield return Vehiculo("QHL158", "105", TipoVehiculo.Camioneta, "Toyota", "Hiace", 2016, 12, [40, 40, 40, 40], activo: false);
    }

    private static T Persona<T>(T p, Guid idEmpresa, string tipoDocumento, string numero, string[] nombres) where T : Persona
    {
        p.IdEmpresa = idEmpresa;
        p.TipoDocumento = tipoDocumento;
        p.NumeroDocumento = numero;
        p.PrimerNombre = nombres[0];
        p.SegundoNombre = nombres[1].Length > 0 ? nombres[1] : null;
        p.PrimerApellido = nombres[2];
        p.SegundoApellido = nombres[3].Length > 0 ? nombres[3] : null;
        return p;
    }

    private static IEnumerable<Empleado> Empleados(Guid idEmpresa, DateOnly hoy)
    {
        Empleado Empleado(string documento, string[] nombres, TipoEmpleado tipo, decimal sueldo, TipoContrato contrato, string telefono,
            (string Categoria, int Dias)? licencia, bool activo = true) => Persona(new Empleado
        {
            TipoEmpleado = tipo, Telefono = telefono, Direccion = "Carrera 5 # 12-40, Neiva", FechaIngreso = new DateOnly(2025, 2, 1),
            Sueldo = sueldo, TipoContrato = contrato, Activo = activo,
            NumeroLicencia = licencia is null ? null : documento,
            CategoriaLicencia = licencia?.Categoria,
            FechaVencimientoLicencia = licencia is { } l ? hoy.AddDays(l.Dias) : null,
        }, idEmpresa, "CC", documento, nombres);

        yield return Empleado("7698123", ["José", "Luis", "Cárdenas", "Rojas"], TipoEmpleado.Conductor, 2_200_000, TipoContrato.TerminoIndefinido, "3101112233", ("C2", 400));
        yield return Empleado("12134567", ["Wilson", "", "Perdomo", "Trujillo"], TipoEmpleado.Conductor, 2_200_000, TipoContrato.TerminoFijo, "3112223344", ("C1", 20));
        yield return Empleado("83045678", ["Édgar", "Alonso", "Vargas", "Cuéllar"], TipoEmpleado.Conductor, 2_100_000, TipoContrato.TerminoFijo, "3123334455", ("C2", -15));
        yield return Empleado("1075212345", ["Diego", "", "Losada", "Ramírez"], TipoEmpleado.Conductor, 2_000_000, TipoContrato.ObraLabor, "3134445566", ("C1", 900));
        yield return Empleado("93123456", ["Ricardo", "", "Polanía", "Medina"], TipoEmpleado.Conductor, 2_000_000, TipoContrato.TerminoIndefinido, "3145556677", ("C3", 200), activo: false);
        yield return Empleado("1075298765", ["Yuli", "Andrea", "Charry", "Ortiz"], TipoEmpleado.Monitora, 1_600_000, TipoContrato.TerminoFijo, "3156667788", null);
        yield return Empleado("36301234", ["Sandra", "Milena", "Tovar", "Ruiz"], TipoEmpleado.Monitora, 1_600_000, TipoContrato.TerminoIndefinido, "3167778899", null);
        yield return Empleado("1077865432", ["Karen", "", "Méndez", "Calderón"], TipoEmpleado.Monitora, 1_500_000, TipoContrato.PrestacionServicios, "3178889900", null);
    }

    private static Dictionary<string, Acudiente> Acudientes(Guid idEmpresa)
    {
        Acudiente Acudiente(string tipo, string documento, string[] nombres, string telefono, string correo, bool activo = true) =>
            Persona(new Acudiente
            {
                Telefono = telefono, Correo = correo.Length > 0 ? correo : null, Direccion = "Calle 10 # 5-23, Neiva", Activo = activo,
            }, idEmpresa, tipo, documento, nombres);

        return new()
        {
            ["acu-1"] = Acudiente("CC", "36181234", ["María", "", "Pérez", "López"], "3001234567", "maria.perez@correo.com"),
            ["acu-2"] = Acudiente("CC", "7712345", ["Carlos", "Andrés", "Rodríguez", "Mejía"], "3109876543", "carlos.rodriguez@correo.com"),
            ["acu-3"] = Acudiente("CC", "55123456", ["Luz", "Marina", "Gómez", "Vargas"], "3157654321", ""),
            ["acu-4"] = Acudiente("CC", "12345678", ["Jorge", "", "Martínez", "Ruiz"], "3204567890", "jorge.martinez@correo.com"),
            ["acu-5"] = Acudiente("CC", "1075234567", ["Andrea", "", "Castillo", "Ortiz"], "3012345678", "andrea.castillo@correo.com"),
            ["acu-6"] = Acudiente("CE", "987654", ["Fernando", "", "Ríos", "Salazar"], "3118765432", ""),
            ["acu-7"] = Acudiente("CC", "1075345678", ["Paola", "Andrea", "Silva", "Torres"], "3165432109", "paola.silva@correo.com"),
            ["acu-8"] = Acudiente("CC", "4567890", ["Hernán", "", "Quintero", "Díaz"], "3176543210", "", activo: false),
        };
    }

    private static IEnumerable<(Estudiante, string Acudiente, string Colegio)> Estudiantes(Guid idEmpresa)
    {
        (Estudiante, string, string) Estudiante(string tipo, string documento, string[] nombres, string nacimiento, string acudiente,
            string colegio, string grado, string jornada, bool activo = true) =>
            (Persona(new Estudiante
            {
                FechaNacimiento = DateOnly.Parse(nacimiento), Direccion = "Calle 10 # 5-23, Neiva", Grado = grado, Jornada = jornada,
                Activo = activo,
            }, idEmpresa, tipo, documento, nombres), acudiente, colegio);

        yield return Estudiante("TI", "1020304050", ["Juan", "Sebastián", "Pérez", "López"], "2013-04-12", "acu-1", "col-5", "7°", "Mañana");
        yield return Estudiante("TI", "1020304051", ["Valentina", "", "Pérez", "López"], "2015-09-03", "acu-1", "col-5", "5°", "Mañana");
        yield return Estudiante("RC", "1122334455", ["Samuel", "David", "Rodríguez", "Mejía"], "2019-02-21", "acu-2", "col-1", "Transición", "Mañana");
        yield return Estudiante("TI", "1098765432", ["Isabella", "", "Gómez", "Vargas"], "2012-11-30", "acu-3", "col-2", "8°", "Tarde");
        yield return Estudiante("TI", "1098765433", ["Mateo", "Andrés", "Gómez", "Vargas"], "2014-06-18", "acu-3", "col-2", "6°", "Tarde");
        yield return Estudiante("CC", "1075123456", ["Daniela", "", "Martínez", "Ruiz"], "2008-01-09", "acu-4", "col-3", "11°", "Única");
        yield return Estudiante("TI", "1075987654", ["Tomás", "", "Martínez", "Ruiz"], "2011-07-25", "acu-4", "col-3", "9°", "Única");
        yield return Estudiante("RC", "1133557799", ["Emiliano", "", "Castillo", "Ortiz"], "2020-03-14", "acu-5", "col-4", "Jardín", "Mañana");
        yield return Estudiante("TI", "1144668800", ["Sara", "Lucía", "Castillo", "Ortiz"], "2016-12-01", "acu-5", "col-4", "4°", "Mañana");
        yield return Estudiante("TI", "1066778899", ["Martín", "", "Ríos", "Salazar"], "2013-10-10", "acu-6", "col-1", "7°", "Tarde", activo: false);
        yield return Estudiante("RC", "1155443322", ["Antonella", "", "Ríos", "Salazar"], "2018-08-08", "acu-6", "col-1", "1°", "Tarde");
        yield return Estudiante("TI", "1088990011", ["Jerónimo", "", "Rodríguez", "Mejía"], "2015-05-05", "acu-2", "col-1", "5°", "Mañana");
    }
}
