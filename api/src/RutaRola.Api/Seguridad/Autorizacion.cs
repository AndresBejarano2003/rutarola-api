using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RutaRola.Api.Errores;
using RutaRola.Application.Autenticacion;
using RutaRola.Application.Comun;
using RutaRola.Domain.Seguridad;
using RutaRola.Infrastructure.Seguridad;

namespace RutaRola.Api.Seguridad;

/// <summary>Exige un permiso (<c>modulo.accion</c> de <see cref="Permisos"/>); cada permiso es una policy.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequierePermisoAttribute(string permiso) : AuthorizeAttribute(permiso);

public static class Autorizacion
{
    /// <summary>Límite por IP para <c>/auth/login</c> y <c>/auth/refresh</c> (fuerza bruta).</summary>
    public const string PoliticaLimiteAutenticacion = "autenticacion";

    public static IServiceCollection AgregarSeguridad(this IServiceCollection services, IConfiguration configuracion)
    {
        services.AddOptions<OpcionesJwt>()
            .Bind(configuracion.GetSection(OpcionesJwt.Seccion))
            .Validate(o => o.Clave.Length >= 32,
                "Falta Jwt:Clave (variable Jwt__Clave o dotnet user-secrets) o tiene menos de 32 caracteres.")
            .Validate(o => o.MinutosAcceso is > 0 and <= 60 && o.DiasRefresh is > 0 and <= 365, "Duración de tokens inválida.")
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<OpcionesJwt>>((o, jwt) =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Value.Emisor,
                    ValidAudience = jwt.Value.Audiencia,
                    IssuerSigningKey = GeneradorTokens.Llave(jwt.Value.Clave),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = Reclamos.Nombre,
                    RoleClaimType = Reclamos.Rol,
                };
                o.Events = new JwtBearerEvents
                {
                    // Los WebSockets del navegador no envían encabezados: SignalR manda el token en la URL
                    // (los logs de peticiones no registran la query string).
                    OnMessageReceived = contexto =>
                    {
                        if (contexto.HttpContext.Request.Path.StartsWithSegments("/hubs")
                            && contexto.Request.Query["access_token"] is { Count: > 0 } token)
                        {
                            contexto.Token = token;
                        }
                        return Task.CompletedTask;
                    },
                    // 401/403 también en ProblemDetails con `mensaje`.
                    OnChallenge = async contexto =>
                    {
                        contexto.HandleResponse();
                        var mensaje = contexto.AuthenticateFailure is null
                            ? "Debes iniciar sesión para continuar."
                            : ServicioAutenticacion.MensajeSesionInvalida;
                        contexto.Response.Headers.WWWAuthenticate = contexto.AuthenticateFailure is null
                            ? "Bearer"
                            : "Bearer error=\"invalid_token\"";
                        await EscribirProblemaAsync(contexto.HttpContext, StatusCodes.Status401Unauthorized, mensaje);
                    },
                    // Si al token le falta un permiso que su rol SÍ tiene hoy (se agregó un permiso después de
                    // iniciar sesión), responde 401: el frontend renueva la sesión (con los permisos actuales) y reintenta.
                    OnForbidden = contexto =>
                    {
                        if (PermisosDesactualizados(contexto.HttpContext.User))
                        {
                            contexto.Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\", error_description=\"permisos desactualizados\"";
                            return EscribirProblemaAsync(contexto.HttpContext, StatusCodes.Status401Unauthorized, ServicioAutenticacion.MensajeSesionInvalida);
                        }
                        return EscribirProblemaAsync(contexto.HttpContext, StatusCodes.Status403Forbidden, DetallesProblema.MensajePorEstado(403));
                    },
                };
            });

        services.AddAuthorizationBuilder()
            // Seguro por defecto: todo exige sesión salvo lo marcado con [AllowAnonymous].
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AgregarPoliticasDePermisos();

        services.AddRateLimiter(o =>
        {
            o.AddPolicy(PoliticaLimiteAutenticacion, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = configuracion.GetValue("Seguridad:IntentosAutenticacionPorMinuto", 10),
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
            o.OnRejected = (contexto, _) =>
            {
                contexto.HttpContext.Response.Headers.RetryAfter = "60";
                return new ValueTask(EscribirProblemaAsync(contexto.HttpContext, StatusCodes.Status429TooManyRequests,
                    "Demasiados intentos. Espera un minuto e intenta de nuevo."));
            };
        });

        return services;
    }

    /// <summary>El token se emitió antes de que su rol recibiera algún permiso del catálogo actual.</summary>
    internal static bool PermisosDesactualizados(System.Security.Claims.ClaimsPrincipal usuario)
    {
        var rol = usuario.FindFirst(Reclamos.Rol)?.Value;
        if (rol is null || !Permisos.PorRol.TryGetValue(rol, out var esperados)) return false;
        var actuales = usuario.FindAll(Reclamos.Permiso).Select(c => c.Value).ToHashSet();
        return esperados.Any(p => !actuales.Contains(p));
    }

    private static AuthorizationBuilder AgregarPoliticasDePermisos(this AuthorizationBuilder builder)
    {
        foreach (var permiso in Permisos.Todos)
        {
            builder.AddPolicy(permiso, p => p.RequireAuthenticatedUser().RequireClaim(Reclamos.Permiso, permiso));
        }
        return builder;
    }

    private static async Task EscribirProblemaAsync(HttpContext http, int estado, string mensaje)
    {
        http.Response.StatusCode = estado;
        var problema = new ProblemDetails { Status = estado, Detail = mensaje };
        problema.Extensions[DetallesProblema.Mensaje] = mensaje;
        await http.RequestServices.GetRequiredService<IProblemDetailsService>()
            .WriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = problema });
    }
}
