using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SGCM.Test.Common.Web;

/// <summary>
/// Endpoint protegido que solo existe en <see cref="ProtectedEndpointApp"/>. Representa "otra
/// función" de la app: cualquier usuario autenticado puede usarlo, pero no resuelve ningún paso
/// pendiente, así que <c>PendingStepFilter</c> debe bloquearlo mientras el usuario tenga uno.
/// </summary>
[ApiController]
[Route(RoutePath)]
[Authorize]
public sealed class ProtectedTestController : ControllerBase
{
    public const string RoutePath = "/api/test/protected";

    [HttpGet]
    public IActionResult Get() => NoContent();
}
