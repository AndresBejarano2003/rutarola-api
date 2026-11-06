using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using RutaRola.Application.Autenticacion;
using RutaRola.Application.Configuracion;
using RutaRola.Application.Empresas;

namespace RutaRola.Application;

public static class DependencyInjection
{
    public static IServiceCollection AgregarAplicacion(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, ServiceLifetime.Singleton);
        services.AddScoped<ServicioAutenticacion>();
        services.AddScoped<ServicioEmpresas>();
        services.AddScoped<ServicioConfiguracion>();
        return services;
    }
}
