using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RutaRola.Application.Comun;
using RutaRola.Infrastructure;
using RutaRola.Infrastructure.Persistencia;
using Testcontainers.PostgreSql;

namespace RutaRola.Tests.Infraestructura;

/// <summary>
/// PostgreSQL real en Docker (Testcontainers) + el API en memoria, compartidos por todas las pruebas
/// de la colección. Se usa Postgres y no InMemory porque los filtros por empresa, los índices únicos
/// parciales y el xmin dependen del motor.
/// </summary>
public sealed class BaseDatosPrueba : IAsyncLifetime
{
    public const string ContrasenaAdmin = "Prueba-Admin-123";
    public const string ContrasenaSuperAdmin = "Prueba-Super-123";
    public const string ClaveJwt = "clave-de-pruebas-solo-para-tests-0123456789abcdef";

    private readonly PostgreSqlContainer contenedor = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public WebApplicationFactory<Program> Api { get; private set; } = null!;
    public string CadenaConexion => contenedor.GetConnectionString();

    public async Task InitializeAsync()
    {
        await contenedor.StartAsync();
        Api = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
        {
            Configurar(web);
        });
        await BaseDatos.MigrarYSembrarAsync(Api.Services);
    }

    /// <summary>Configuración común de las pruebas (sobrescribible en una fábrica derivada).</summary>
    public void Configurar(Microsoft.AspNetCore.Hosting.IWebHostBuilder web)
    {
        web.UseEnvironment("Testing");
        web.UseSetting($"ConnectionStrings:{DependencyInjection.NombreConexion}", CadenaConexion);
        web.UseSetting("Seed:Admin:Password", ContrasenaAdmin);
        web.UseSetting("Seed:SuperAdmin:Password", ContrasenaSuperAdmin);
        web.UseSetting("BaseDatos:MigrarAlIniciar", "false");
        // El proceso periódico no corre en las pruebas (se invoca a mano); los canales quedan simulados.
        web.UseSetting("Procesos:Habilitado", "false");
        web.UseSetting("Mensajeria:Whatsapp", "Simulado");
        web.UseSetting("Mensajeria:Correo", "Simulado");
        web.UseSetting("Jwt:Clave", ClaveJwt);
        // Sin gracia: reusar un refresh rotado se trata siempre como robo (comportamiento determinista).
        web.UseSetting("Jwt:SegundosGraciaRotacion", "0");
        web.UseSetting("Seguridad:IntentosAutenticacionPorMinuto", "10000");
    }

    public async Task DisposeAsync()
    {
        await Api.DisposeAsync();
        await contenedor.DisposeAsync();
    }

    /// <summary>DbContext "como si" la petición viniera de un usuario de <paramref name="idEmpresa"/>.</summary>
    public RutaRolaDbContext Contexto(Guid? idEmpresa, bool modoSistema = false)
    {
        var opciones = new DbContextOptionsBuilder<RutaRolaDbContext>();
        DependencyInjection.Configurar(opciones, CadenaConexion);
        var db = new RutaRolaDbContext(opciones.Options, new ContextoFijo(idEmpresa), TimeProvider.System);
        if (modoSistema)
        {
            db.UsarModoSistema();
        }
        return db;
    }

    public async Task<T> EnScope<T>(Func<IServiceProvider, Task<T>> accion)
    {
        await using var scope = Api.Services.CreateAsyncScope();
        return await accion(scope.ServiceProvider);
    }

    private sealed class ContextoFijo(Guid? idEmpresa) : IContextoUsuario
    {
        public bool Autenticado => idEmpresa is not null;
        public Guid? IdUsuario => null;
        public Guid? IdEmpresa => idEmpresa;
        public string? Rol => null;
        public Guid? IdEmpleado => null;
        public Guid? IdAcudiente => null;
        public Guid? IdSesion => null;
        public string? DireccionIp => null;
    }
}

[CollectionDefinition(Nombre)]
public class ColeccionBaseDatos : ICollectionFixture<BaseDatosPrueba>
{
    public const string Nombre = "BaseDatos";
}
