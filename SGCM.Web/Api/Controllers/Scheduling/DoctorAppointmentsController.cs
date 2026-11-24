using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Scheduling.Commands.CancelAppointmentByDoctor;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Web.Api.Infrastructure;
using SGCM.Web.Contracts.Scheduling;
using SGCM.Web.Mappers.Scheduling;

namespace SGCM.Web.Api.Controllers.Scheduling;

[Route("api/doctors/me/appointments")]
public sealed class DoctorAppointmentsController : ApiControllerBase
{
    /// <summary>Cancela una cita pendiente o confirmada del médico autenticado.</summary>
    /// <remarks>
    /// Se permite en cualquier momento y siempre con motivo. Dentro del umbral de poco aviso configurado
    /// (Scheduling:DoctorLateCancellationThresholdHours) el motivo debe tener el mínimo de palabras configurado
    /// (Scheduling:DoctorLateCancellationMinimumWords); si no, responde 400. Una cita ajena o inexistente
    /// responde 404; una cita rechazada, cancelada o completada responde 409. El cuerpo es obligatorio.
    /// </remarks>
    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = nameof(UserRole.Doctor))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(
        int id,
        CancelAppointmentByDoctorRequest request,
        [FromServices] ICommandHandler<CancelAppointmentByDoctorCommand, Unit> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToCommand(id), cancellationToken);

        return result.IsSuccess ? NoContent() : ErrorResult(result.Error);
    }
}
