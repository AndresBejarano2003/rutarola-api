using Microsoft.AspNetCore.Mvc.Filters;
using RutaRola.Application.Suscripciones;

namespace RutaRola.Api.Seguridad;

/// <summary>
/// La acción exige una función del plan de la empresa (<see cref="FuncionPlan"/>): sin ella responde 403 con el
/// mensaje "Tu plan X no incluye … Está disponible desde el plan Y". Con <paramref name="soloEscritura"/> los GET
/// pasan (p. ej. ver las programaciones guardadas, pero no crear nuevas). Va después de los permisos del rol.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequiereFuncionPlanAttribute(string funcion, bool soloEscritura = false) : Attribute, IAsyncActionFilter, IOrderedFilter
{
    public string Funcion { get; } = funcion;

    /// <summary>Antes de la validación de la solicitud: sin la función no importa si los datos son válidos.</summary>
    public int Order => -1000;

    public async Task OnActionExecutionAsync(ActionExecutingContext contexto, ActionExecutionDelegate siguiente)
    {
        var metodo = contexto.HttpContext.Request.Method;
        if (!(soloEscritura && (HttpMethods.IsGet(metodo) || HttpMethods.IsHead(metodo))))
        {
            var suscripcion = contexto.HttpContext.RequestServices.GetRequiredService<ServicioSuscripcion>();
            await suscripcion.ExigirFuncionAsync(Funcion, contexto.HttpContext.RequestAborted);
        }
        await siguiente();
    }
}
