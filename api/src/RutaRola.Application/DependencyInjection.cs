using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using RutaRola.Application.Autenticacion;
using RutaRola.Application.Configuracion;
using RutaRola.Application.Empresas;
using RutaRola.Application.Operacion;
using RutaRola.Application.Personas;

namespace RutaRola.Application;

public static class DependencyInjection
{
    public static IServiceCollection AgregarAplicacion(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, ServiceLifetime.Singleton);
        services.AddScoped<ServicioAutenticacion>();
        services.AddScoped<ServicioEmpresas>();
        services.AddScoped<ServicioConfiguracion>();
        services.AddScoped<ServicioAcceso>();
        services.AddScoped<ServicioEmpleados>();
        services.AddScoped<ServicioAcudientes>();
        services.AddScoped<ServicioEstudiantes>();
        services.AddScoped<ServicioColegios>();
        services.AddScoped<ServicioVehiculos>();
        services.AddScoped<ServicioConductores>();
        services.AddScoped<ServicioRutas>();
        services.AddScoped<ServicioAsignaciones>();
        services.AddScoped<ServicioRecorridos>();
        services.AddScoped<ServicioGps>();
        return services;
    }
}
