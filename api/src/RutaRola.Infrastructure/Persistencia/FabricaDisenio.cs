using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using RutaRola.Application.Comun;

namespace RutaRola.Infrastructure.Persistencia;

/// <summary>
/// Para <c>dotnet ef</c> (crear migraciones) sin arrancar el API. Toma la conexión de
/// <c>ConnectionStrings__RutaRola</c> si existe (solo se necesita para <c>database update</c>).
/// </summary>
public class FabricaDisenio : IDesignTimeDbContextFactory<RutaRolaDbContext>
{
    public RutaRolaDbContext CreateDbContext(string[] args)
    {
        var cadena = Environment.GetEnvironmentVariable($"ConnectionStrings__{DependencyInjection.NombreConexion}")
            ?? "Host=localhost;Port=5432;Database=rutarola;Username=rutarola";
        var opciones = new DbContextOptionsBuilder<RutaRolaDbContext>();
        DependencyInjection.Configurar(opciones, cadena);
        return new RutaRolaDbContext(opciones.Options, new ContextoSinUsuario(), TimeProvider.System);
    }

    private sealed class ContextoSinUsuario : IContextoUsuario
    {
        public bool Autenticado => false;
        public Guid? IdUsuario => null;
        public Guid? IdEmpresa => null;
        public string? Rol => null;
        public Guid? IdEmpleado => null;
        public Guid? IdAcudiente => null;
        public Guid? IdSesion => null;
        public string? DireccionIp => null;
    }
}
