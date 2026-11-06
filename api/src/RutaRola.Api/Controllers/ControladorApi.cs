using Microsoft.AspNetCore.Mvc;

namespace RutaRola.Api.Controllers;

/// <summary>Base de los controladores: <c>/api/v1/&lt;controlador&gt;</c> en minúsculas, JSON, ProblemDetails.</summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public abstract class ControladorApi : ControllerBase;
