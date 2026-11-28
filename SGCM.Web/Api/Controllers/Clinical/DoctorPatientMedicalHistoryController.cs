using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Clinical.Queries.GetMedicalHistory;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Web.Api.Infrastructure;
using SGCM.Web.Contracts.Clinical;
using SGCM.Web.Mappers.Clinical;

namespace SGCM.Web.Api.Controllers.Clinical;

[Route("api/doctors/me/patients")]
public sealed class DoctorPatientMedicalHistoryController : ApiControllerBase
{
    /// <summary>Devuelve el historial médico de un paciente al médico autenticado.</summary>
    /// <remarks>
    /// Solo si el médico tiene al menos una cita con el paciente, en cualquier estado. Incluye las citas completadas
    /// del paciente, tengan o no registro clínico, y cualquier otra con registro, con todos sus médicos. De cada cita
    /// trae la versión vigente y, en las citas que atendió este médico, también las anteriores. Cada versión mostrada
    /// queda en el registro de accesos con el usuario, la hora y la IP.
    /// 400 responde MedicalHistory.InvalidPatientId si el id no es mayor que cero. 403 responde
    /// MedicalHistory.NoAppointmentWithPatient si el médico no tiene citas con el paciente o el paciente no existe,
    /// sin distinguir entre ambos casos. 404 responde Doctor.NotFound si el médico no tiene perfil activo. Un id que no
    /// es entero no coincide con la ruta y responde 404.
    /// </remarks>
    /// <param name="patientId">Id del paciente, mayor que cero.</param>
    /// <param name="handler">Manejador de la consulta.</param>
    /// <param name="cancellationToken">Token de cancelación de la petición.</param>
    [HttpGet("{patientId:int}/medical-history")]
    [Authorize(Roles = nameof(UserRole.Doctor))]
    [ProducesResponseType(typeof(MedicalHistoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        int patientId,
        [FromServices] IQueryHandler<GetMedicalHistoryQuery, MedicalHistoryDto> handler,
        CancellationToken cancellationToken)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await handler.HandleAsync(new GetMedicalHistoryQuery(patientId, ipAddress), cancellationToken);

        return result.IsSuccess ? Ok(result.Value.ToResponse()) : ErrorResult(result.Error);
    }
}
