using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Scheduling.Commands.CancelAppointment;
using SGCM.Data.Application.Scheduling.Commands.RequestAppointment;
using SGCM.Data.Application.Scheduling.Queries.GetDoctorAssignment;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Web.Api.Infrastructure;
using SGCM.Web.Contracts.Scheduling;
using SGCM.Web.Mappers.Scheduling;

namespace SGCM.Web.Api.Controllers.Scheduling;

[Route("api/appointments")]
public sealed class AppointmentsController : ApiControllerBase
{
    /// <summary>Asigna un médico para el resumen de la solicitud, cuando el paciente elige solo la especialidad.</summary>
    /// <remarks>
    /// Elige al azar uno de los médicos activos de la especialidad, dentro de los filtros de ubicación, que tenga
    /// libre el horario, y devuelve su nombre y su tarifa. No reserva el horario: al enviar la solicitud se vuelve
    /// a comprobar. Un horario a menos de la antelación mínima se rechaza indicando que debe llamar al médico.
    /// </remarks>
    [HttpGet("doctor-assignment")]
    [Authorize(Roles = nameof(UserRole.Patient))]
    [ProducesResponseType(typeof(DoctorAssignmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AssignDoctor(
        [FromQuery] DoctorAssignmentRequest request,
        [FromServices] IQueryHandler<GetDoctorAssignmentQuery, DoctorAssignmentDto> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToQuery(), cancellationToken);

        return result.IsSuccess ? Ok(result.Value.ToResponse()) : ErrorResult(result.Error);
    }

    /// <summary>Solicita una cita para el paciente autenticado; la cita queda pendiente de 2 horas.</summary>
    /// <remarks>
    /// El paciente necesita su perfil completo. Si eligió al médico y ese horario ya no está libre, se rechaza con
    /// "El horario no está disponible". Si el médico lo asignó el resumen y perdió el horario, se asigna otro médico
    /// libre de la especialidad y la respuesta lo indica con <c>doctorChanged</c>; solo se rechaza si no queda
    /// ninguno. También se rechaza si el horario se traslapa con otra cita activa del paciente.
    /// </remarks>
    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Patient))]
    [ProducesResponseType(typeof(RequestAppointmentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        RequestAppointmentRequest request,
        [FromServices] ICommandHandler<RequestAppointmentCommand, RequestedAppointmentDto> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToCommand(), cancellationToken);

        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value.ToResponse())
            : ErrorResult(result.Error);
    }

    /// <summary>Cancela una cita pendiente o confirmada del paciente autenticado.</summary>
    /// <remarks>
    /// Se permite dentro del plazo de cancelación configurado (Scheduling:PatientCancellationDeadlineHours), con
    /// un motivo opcional. Fuera de plazo responde 409 indicando que debe llamar al médico. Una cita ajena o
    /// inexistente responde 404; una cita rechazada, cancelada o completada responde 409. El cuerpo es
    /// obligatorio; sin motivo se envía {} o { "reason": null }.
    /// </remarks>
    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = nameof(UserRole.Patient))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(
        int id,
        CancelAppointmentRequest request,
        [FromServices] ICommandHandler<CancelAppointmentCommand, Unit> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToCommand(id), cancellationToken);

        return result.IsSuccess ? NoContent() : ErrorResult(result.Error);
    }
}
