using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Clinical.Queries.GetMedicalHistory;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Web.Api.Infrastructure;
using SGCM.Web.Contracts.Clinical;
using SGCM.Web.Mappers.Clinical;

namespace SGCM.Web.Api.Controllers.Clinical;

[Route("api/patients/me/medical-history")]
public sealed class PatientMedicalHistoryController : ApiControllerBase
{
    /// <summary>Devuelve el historial médico del paciente autenticado.</summary>
    /// <remarks>
    /// Incluye sus citas completadas, tengan o no registro clínico, y cualquier otra con registro, de la más reciente
    /// a la más antigua, con la fecha, el médico y la especialidad. De cada cita trae solo la versión vigente del
    /// registro; una cita sin registro viene con HasMedicalRecord = false. Cada versión mostrada queda en el registro
    /// de accesos con el usuario, la hora y la IP. 404 responde Patient.ProfileNotFound si el usuario aún no completó
    /// su perfil de paciente.
    /// </remarks>
    /// <param name="handler">Manejador de la consulta.</param>
    /// <param name="cancellationToken">Token de cancelación de la petición.</param>
    [HttpGet]
    [Authorize(Roles = nameof(UserRole.Patient))]
    [ProducesResponseType(typeof(MedicalHistoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        [FromServices] IQueryHandler<GetMedicalHistoryQuery, MedicalHistoryDto> handler,
        CancellationToken cancellationToken)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await handler.HandleAsync(new GetMedicalHistoryQuery(PatientId: null, ipAddress), cancellationToken);

        return result.IsSuccess ? Ok(result.Value.ToResponse()) : ErrorResult(result.Error);
    }
}
