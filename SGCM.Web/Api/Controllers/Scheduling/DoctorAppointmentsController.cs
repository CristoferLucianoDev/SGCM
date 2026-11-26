using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Scheduling.Commands.CancelAppointmentByDoctor;
using SGCM.Data.Application.Scheduling.Commands.ConfirmAppointment;
using SGCM.Data.Application.Scheduling.Commands.RejectAppointment;
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

    /// <summary>Confirma una cita pendiente del médico autenticado.</summary>
    /// <remarks>
    /// Solo citas propias y pendientes, y hasta el plazo de confirmación configurado
    /// (Scheduling:ConfirmationDeadlineHours). 409 responde Appointment.NotPending si la cita no está pendiente y
    /// Appointment.ConfirmationDeadlinePassed si venció el plazo. Una cita ajena o inexistente responde 404.
    /// Appointment.InvalidAppointmentId responde 400 con code si el id no es mayor que cero. Un id que no es entero no
    /// coincide con la ruta y responde 404.
    /// </remarks>
    /// <param name="id">Id de la cita, mayor que cero.</param>
    /// <param name="handler">Manejador del comando.</param>
    /// <param name="cancellationToken">Token de cancelación de la petición.</param>
    [HttpPost("{id:int}/confirm")]
    [Authorize(Roles = nameof(UserRole.Doctor))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Confirm(
        int id,
        [FromServices] ICommandHandler<ConfirmAppointmentCommand, Unit> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ConfirmAppointmentCommand(id), cancellationToken);

        return result.IsSuccess ? NoContent() : ErrorResult(result.Error);
    }

    /// <summary>Rechaza una cita pendiente del médico autenticado.</summary>
    /// <remarks>
    /// Solo citas propias y pendientes, y hasta el plazo de confirmación configurado
    /// (Scheduling:ConfirmationDeadlineHours). El motivo es obligatorio, hasta 500 caracteres: 400 responde
    /// Appointment.StatusReasonRequired si falta y Appointment.StatusReasonTooLong si es muy largo.
    /// 409 responde Appointment.NotPending si la cita no está pendiente y Appointment.ConfirmationDeadlinePassed si
    /// venció el plazo. Una cita ajena o inexistente responde 404. Los errores de forma de la petición (cuerpo mal
    /// formado) responden 400 sin code; un id que no es entero no coincide con la ruta y responde 404.
    /// </remarks>
    /// <param name="id">Id de la cita, mayor que cero.</param>
    /// <param name="request">Motivo del rechazo.</param>
    /// <param name="handler">Manejador del comando.</param>
    /// <param name="cancellationToken">Token de cancelación de la petición.</param>
    [HttpPost("{id:int}/reject")]
    [Authorize(Roles = nameof(UserRole.Doctor))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reject(
        int id,
        RejectAppointmentRequest request,
        [FromServices] ICommandHandler<RejectAppointmentCommand, Unit> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToCommand(id), cancellationToken);

        return result.IsSuccess ? NoContent() : ErrorResult(result.Error);
    }
}
