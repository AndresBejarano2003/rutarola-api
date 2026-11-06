using FluentValidation;
using Microsoft.AspNetCore.Mvc.Filters;

namespace RutaRola.Api.Configuracion;

/// <summary>
/// Valida con FluentValidation cada argumento de la acción que tenga un <see cref="IValidator{T}"/>
/// registrado; si falla lanza <see cref="ValidationException"/> (→ 400 con <c>mensaje</c> y <c>errores</c>).
/// </summary>
public class FiltroValidacion(IServiceProvider servicios) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext contexto, ActionExecutionDelegate siguiente)
    {
        foreach (var argumento in contexto.ActionArguments.Values.OfType<object>())
        {
            var tipoValidador = typeof(IValidator<>).MakeGenericType(argumento.GetType());
            if (servicios.GetService(tipoValidador) is IValidator validador)
            {
                var resultado = await validador.ValidateAsync(new ValidationContext<object>(argumento), contexto.HttpContext.RequestAborted);
                if (!resultado.IsValid)
                {
                    throw new ValidationException(resultado.Errors);
                }
            }
        }
        await siguiente();
    }
}
