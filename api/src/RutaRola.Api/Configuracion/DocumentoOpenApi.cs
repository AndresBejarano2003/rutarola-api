using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace RutaRola.Api.Configuracion;

/// <summary>Título del documento y esquema Bearer (JWT) para probar endpoints protegidos desde /docs.</summary>
public class DocumentoOpenApi : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument documento, OpenApiDocumentTransformerContext contexto, CancellationToken ct)
    {
        documento.Info.Title = "RutaRola API";
        documento.Info.Version = "v1";
        documento.Info.Description =
            "API de RutaRola (transporte escolar multiempresa). Rutas bajo /api/v1. " +
            "Autenticación: POST /api/v1/auth/login → usa tokens.accessToken como Bearer.";

        documento.Components ??= new OpenApiComponents();
        documento.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        documento.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Access token de POST /api/v1/auth/login (vida de 15 minutos).",
        };
        documento.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", documento)] = [] }];
        return Task.CompletedTask;
    }
}
