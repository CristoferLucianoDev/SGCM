using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Clinical.Commands.CreateMedicalRecordVersion;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Web.Api.Infrastructure;
using SGCM.Web.Contracts.Clinical;
using SGCM.Web.Mappers.Clinical;

namespace SGCM.Web.Api.Controllers.Clinical;

[Route("api/doctors/me/appointments")]
public sealed class DoctorMedicalRecordsController : ApiControllerBase
{
    /// <summary>Registra la información clínica de una cita del médico autenticado.</summary>
    /// <remarks>
    /// La primera vez crea el registro de la cita; después, cada envío crea una versión nueva que pasa a ser la vigente
    /// y la anterior se conserva sin cambios (IsCorrection = true). Cada creación queda en el registro de accesos con
    /// el usuario, la hora y la IP. Solo citas propias confirmadas o completadas, desde su hora de inicio.
    /// 400 con code responde MedicalRecord.Empty si las tres partes están vacías, MedicalRecord.DiagnosisTooLong,
    /// MedicalRecord.TreatmentTooLong o MedicalRecord.NotesTooLong si superan su longitud, y
    /// Appointment.InvalidAppointmentId si el id no es mayor que cero. 404 responde Appointment.NotFound si la cita es
    /// ajena o no existe. 409 responde Appointment.ClinicalRecordInvalidStatus si la cita está pendiente, rechazada o
    /// cancelada, y Appointment.ClinicalRecordNotStarted si aún no empieza. Los errores de forma de la petición
    /// (cuerpo mal formado) responden 400 sin code; un id que no es entero no coincide con la ruta y responde 404.
    /// </remarks>
    /// <param name="id">Id de la cita, mayor que cero.</param>
    /// <param name="request">Diagnóstico, tratamiento y notas.</param>
    /// <param name="handler">Manejador del comando.</param>
    /// <param name="cancellationToken">Token de cancelación de la petición.</param>
    [HttpPost("{id:int}/medical-record")]
    [Authorize(Roles = nameof(UserRole.Doctor))]
    [ProducesResponseType(typeof(MedicalRecordVersionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateVersion(
        int id,
        CreateMedicalRecordVersionRequest request,
        [FromServices] ICommandHandler<CreateMedicalRecordVersionCommand, MedicalRecordVersionResult> handler,
        CancellationToken cancellationToken)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await handler.HandleAsync(request.ToCommand(id, ipAddress), cancellationToken);

        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value.ToResponse())
            : ErrorResult(result.Error);
    }
}
