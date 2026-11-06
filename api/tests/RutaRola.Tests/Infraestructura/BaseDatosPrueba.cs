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

    private readonly PostgreSqlContainer contenedor = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public WebApplicationFactory<Program> Api { get; private set; } = null!;
    public string CadenaConexion => contenedor.GetConnectionString();

    public async Task InitializeAsync()
    {
        await contenedor.StartAsync();
        Api = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
        {
            web.UseEnvironment("Testing");
            web.UseSetting($"ConnectionStrings:{DependencyInjection.NombreConexion}", CadenaConexion);
            web.UseSetting("Seed:Admin:Password", ContrasenaAdmin);
            web.UseSetting("BaseDatos:MigrarAlIniciar", "false");
        });
        await BaseDatos.MigrarYSembrarAsync(Api.Services);
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
        public string? DireccionIp => null;
    }
}

[CollectionDefinition(Nombre)]
public class ColeccionBaseDatos : ICollectionFixture<BaseDatosPrueba>
{
    public const string Nombre = "BaseDatos";
}
