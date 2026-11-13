using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using RutaRola.Application.Autenticacion;
using RutaRola.Application.Comunicacion;
using RutaRola.Application.Configuracion;
using RutaRola.Application.Empresas;
using RutaRola.Application.Finanzas;
using RutaRola.Application.Operacion;
using RutaRola.Application.Personas;

namespace RutaRola.Application;

public static class DependencyInjection
{
    public static IServiceCollection AgregarAplicacion(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, ServiceLifetime.Singleton);
        services.AddScoped<ServicioAutenticacion>();
        services.AddScoped<ServicioPerfil>();
        services.AddScoped<ServicioEmpresas>();
        services.AddScoped<ServicioAdministradoresEmpresa>();
        services.AddScoped<ServicioConfiguracion>();
        services.AddScoped<ParametrosEmpresa>();
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
        services.AddScoped<ServicioFacturas>();
        services.AddScoped<ServicioRecibos>();
        services.AddScoped<ServicioCotizaciones>();
        services.AddScoped<ServicioProgramaciones>();
        services.AddScoped<ServicioReportes>();
        services.AddScoped<ServicioTablero>();
        services.AddScoped<GeneradorNotificaciones>();
        services.AddSingleton<RevisionNotificaciones>();
        services.AddScoped<ServicioNotificaciones>();
        services.AddScoped<ServicioCobros>();
        services.AddScoped<ServicioPortal>();
        services.AddScoped<ServicioMensajesWhatsapp>();
        services.AddScoped<Soporte.AvisosSoporte>();
        services.AddScoped<Soporte.ServicioSoporte>();
        services.AddScoped<Suscripciones.ServicioSuscripcion>();
        services.AddScoped<Suscripciones.ServicioCodigosPromocionales>();
        services.AddScoped<Suscripciones.ServicioParametrosPlataforma>();
        services.AddScoped<Operacion.ServicioAvisosAusencia>();
        services.AddScoped<Operacion.ServicioDocumentosVehiculo>();
        services.AddScoped<ServicioFormatoDocumentos>();
        services.AddScoped<Empresas.ServicioRegistro>();
        return services;
    }
}
